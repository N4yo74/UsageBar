using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using CodexBarWin.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexBarWin.App;

/// <summary>
/// Result of a successful <see cref="ClaudeWebViewService.FetchAsync"/> call -
/// the exact percent-based usage numbers from claude.ai's own usage API.
/// </summary>
public sealed record ClaudeLiveUsage
{
    public int? SessionPercent { get; init; }
    public DateTimeOffset? SessionResetsAt { get; init; }
    public int? WeeklyPercent { get; init; }
    public DateTimeOffset? WeeklyResetsAt { get; init; }
    public List<ScopedLimit> ScopedLimits { get; init; } = new();
}

/// <summary>
/// Owns a single hidden WebView2 instance logged into claude.ai under the
/// app's own (isolated) profile - never Claude Desktop's cookies/tokens.
///
/// The host window is created once, kept off-screen, and reused for every
/// fetch. When the user needs to log in (first run, or a fetch comes back
/// unauthorized), <see cref="ShowLoginWindow"/> moves it on-screen so the
/// user can complete the claude.ai login flow; <see cref="HideLoginWindow"/>
/// moves it back off-screen once a fetch succeeds.
/// </summary>
public sealed class ClaudeWebViewService : IDisposable
{
    private const double OffscreenLeft = -32000;
    private const double OffscreenTop = -32000;

    private readonly Window _window;
    private readonly WebView2 _webView;
    private Task<bool>? _initTask;
    private bool _disposed;

    public ClaudeWebViewService()
    {
        _webView = new WebView2();

        _window = new Window
        {
            Title = "CodexBarWin - Claude ログイン",
            Width = 480,
            Height = 760,
            ShowInTaskbar = false,
            Content = _webView,
            Left = OffscreenLeft,
            Top = OffscreenTop,
        };

        // The user can close the login window with the title bar's X button;
        // treat that the same as "hide" so the WebView2 (and its login state)
        // stays alive for the next scheduled fetch.
        _window.Closing += (_, e) =>
        {
            if (_disposed)
            {
                return; // real shutdown (Dispose) - let it close.
            }

            e.Cancel = true;
            HideLoginWindow();
        };
    }

    /// <summary>Safe, non-secret reason the most recent <see cref="FetchAsync"/> failed. Null on success.</summary>
    public string? LastFetchError { get; private set; }

    /// <summary>True while the login window is positioned on-screen for the user.</summary>
    public bool IsLoginWindowVisible { get; private set; }

    /// <summary>
    /// Creates the CoreWebView2 (isolated profile under %LOCALAPPDATA%\CodexBarWin\WebView2)
    /// and navigates to claude.ai. Safe to call repeatedly - only does the work once.
    /// Returns false (never throws) if the WebView2 Runtime isn't installed or anything
    /// else goes wrong; <see cref="LastFetchError"/> is set to "webview2_missing" or
    /// "other:&lt;kind&gt;" in that case.
    /// </summary>
    public Task<bool> EnsureInitializedAsync()
    {
        _initTask ??= InitializeCoreAsync();
        return _initTask;
    }

    private async Task<bool> InitializeCoreAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexBarWin", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            // Show() is required for the WebView2 template/HWND to materialize; the window
            // is parked off-screen so nothing is visible to the user at this point.
            _window.Show();

            await _webView.EnsureCoreWebView2Async(environment);

            var navTcs = new TaskCompletionSource();
            void OnNavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                navTcs.TrySetResult();
            }
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.Navigate("https://claude.ai/");
            await navTcs.Task;

            return true;
        }
        catch (Exception ex)
        {
            LastFetchError = IsWebView2RuntimeMissing(ex) ? "webview2_missing" : $"other:{ex.GetType().Name}";
            return false;
        }
    }

    private static bool IsWebView2RuntimeMissing(Exception ex) =>
        ex is WebView2RuntimeNotFoundException
        || ex.GetType().Name.Contains("WebView2RuntimeNotFound", StringComparison.Ordinal);

    /// <summary>
    /// Fetches live usage via the same-origin fetch calls documented against claude.ai:
    /// GET /api/organizations -> first org uuid -> GET /api/organizations/{uuid}/usage.
    /// Returns null on any failure (not logged in, network error, unexpected shape, etc);
    /// check <see cref="LastFetchError"/> for a safe diagnostic reason.
    /// </summary>
    public async Task<ClaudeLiveUsage?> FetchAsync()
    {
        LastFetchError = null;

        var initialized = await EnsureInitializedAsync();
        if (!initialized)
        {
            LastFetchError ??= "webview2_missing";
            return null;
        }

        string inner;
        try
        {
            // NOTE: ExecuteScriptAsync's own return value is unreliable for a script whose
            // completion value is a Promise - in testing it came back as "{}" (the JSON
            // shape of an unresolved Promise object) instead of waiting for resolution.
            // So the script posts its result back via window.chrome.webview.postMessage
            // instead, and we wait for that message - postMessage delivers the exact
            // string with no extra JSON-encoding layer to peel off.
            inner = await RunScriptAndAwaitMessageAsync(FetchScript, TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            LastFetchError = "other:timeout";
            return null;
        }
        catch (Exception ex)
        {
            LastFetchError = $"other:{ex.GetType().Name}";
            return null;
        }

        if (string.IsNullOrWhiteSpace(inner))
        {
            LastFetchError = "not_logged_in";
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inner);
        }
        catch (JsonException)
        {
            // Most likely a login/HTML page or some other non-JSON body.
            LastFetchError = "not_logged_in";
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("__error", out var errProp) && errProp.ValueKind == JsonValueKind.String)
            {
                LastFetchError = errProp.GetString() ?? "other:unknown";
                return null;
            }

            var result = ParseUsage(root);
            if (result is null)
            {
                LastFetchError = "other:unexpected_shape";
                return null;
            }

            return result;
        }
    }

    /// <summary>
    /// Runs <paramref name="script"/> (fire-and-forget - its own return value is ignored)
    /// and waits for the one <c>window.chrome.webview.postMessage(...)</c> call it's
    /// expected to make with the result, or throws <see cref="TimeoutException"/> if none
    /// arrives within <paramref name="timeout"/>.
    /// </summary>
    private async Task<string> RunScriptAndAwaitMessageAsync(string script, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? s, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try
            {
                message = e.TryGetWebMessageAsString();
            }
            catch
            {
                message = e.WebMessageAsJson;
            }

            tcs.TrySetResult(message);
        }

        _webView.CoreWebView2.WebMessageReceived += Handler;
        try
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync(script).ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        tcs.TrySetException(t.Exception!);
                    }
                },
                TaskScheduler.Default);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            if (completed != tcs.Task)
            {
                throw new TimeoutException("claude.ai usage フェッチがタイムアウトしました");
            }

            return await tcs.Task;
        }
        finally
        {
            _webView.CoreWebView2.WebMessageReceived -= Handler;
        }
    }

    /// <summary>Moves the hidden window on-screen (centered) so the user can log in.</summary>
    public void ShowLoginWindow()
    {
        var workArea = SystemParameters.WorkArea;
        _window.Left = workArea.Left + (workArea.Width - _window.Width) / 2;
        _window.Top = workArea.Top + (workArea.Height - _window.Height) / 2;
        _window.Topmost = true;
        _window.Show();
        _window.Activate();
        IsLoginWindowVisible = true;
    }

    /// <summary>Moves the window back off-screen; the WebView2/session stays alive.</summary>
    public void HideLoginWindow()
    {
        _window.Topmost = false;
        _window.Left = OffscreenLeft;
        _window.Top = OffscreenTop;
        IsLoginWindowVisible = false;
    }

    // Fire-and-forget from ExecuteScriptAsync's point of view: the outer IIFE returns
    // synchronously (a plain string), and the actual fetch work happens in the inner
    // async IIFE, which reports its result back via postMessage instead of a return
    // value (see the comment on RunScriptAndAwaitMessageAsync for why).
    private const string FetchScript = """
        (function () {
            (async () => {
                try {
                    const orgsResp = await fetch('/api/organizations', { headers: { accept: 'application/json' } });
                    if (!orgsResp.ok) {
                        const kind = (orgsResp.status === 401 || orgsResp.status === 403) ? 'unauthorized' : ('other:http_' + orgsResp.status);
                        window.chrome.webview.postMessage(JSON.stringify({ __error: kind }));
                        return;
                    }
                    const orgs = await orgsResp.json();
                    if (!Array.isArray(orgs) || orgs.length === 0 || !orgs[0] || !orgs[0].uuid) {
                        window.chrome.webview.postMessage(JSON.stringify({ __error: 'other:no_orgs' }));
                        return;
                    }
                    const uuid = orgs[0].uuid;
                    const usageResp = await fetch('/api/organizations/' + uuid + '/usage', { headers: { accept: 'application/json' } });
                    if (!usageResp.ok) {
                        const kind = (usageResp.status === 401 || usageResp.status === 403) ? 'unauthorized' : ('other:http_' + usageResp.status);
                        window.chrome.webview.postMessage(JSON.stringify({ __error: kind }));
                        return;
                    }
                    const u = await usageResp.json();
                    window.chrome.webview.postMessage(JSON.stringify(u));
                } catch (e) {
                    window.chrome.webview.postMessage(JSON.stringify({ __error: 'other:exception' }));
                }
            })();
            return 'started';
        })()
        """;

    private static ClaudeLiveUsage? ParseUsage(JsonElement root)
    {
        int? sessionPercent = null;
        DateTimeOffset? sessionResetsAt = null;
        int? weeklyPercent = null;
        DateTimeOffset? weeklyResetsAt = null;
        var scopedLimits = new List<ScopedLimit>();

        var usedLimitsArray = false;

        if (root.TryGetProperty("limits", out var limits)
            && limits.ValueKind == JsonValueKind.Array
            && limits.GetArrayLength() > 0)
        {
            usedLimitsArray = true;

            foreach (var limit in limits.EnumerateArray())
            {
                var kind = GetString(limit, "kind");
                var percent = GetInt(limit, "percent");
                var resetsAt = GetDateTimeOffset(limit, "resets_at");

                if (kind == "session")
                {
                    sessionPercent = percent;
                    sessionResetsAt = resetsAt;
                }
                else if (kind == "weekly_all")
                {
                    weeklyPercent = percent;
                    weeklyResetsAt = resetsAt;
                }
                else if (kind == "weekly_scoped" || limit.TryGetProperty("scope", out _))
                {
                    var displayName = "unknown";
                    if (limit.TryGetProperty("scope", out var scope)
                        && scope.TryGetProperty("model", out var model)
                        && model.TryGetProperty("display_name", out var dn)
                        && dn.ValueKind == JsonValueKind.String)
                    {
                        displayName = dn.GetString() ?? displayName;
                    }

                    scopedLimits.Add(new ScopedLimit
                    {
                        DisplayName = displayName,
                        Percent = percent ?? 0,
                        ResetsAt = resetsAt,
                    });
                }
            }
        }

        if (!usedLimitsArray && root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("five_hour", out var fiveHour) && fiveHour.ValueKind == JsonValueKind.Object)
            {
                sessionPercent = GetInt(fiveHour, "utilization");
                sessionResetsAt = GetDateTimeOffset(fiveHour, "resets_at");
            }

            if (usage.TryGetProperty("seven_day", out var sevenDay) && sevenDay.ValueKind == JsonValueKind.Object)
            {
                weeklyPercent = GetInt(sevenDay, "utilization");
                weeklyResetsAt = GetDateTimeOffset(sevenDay, "resets_at");
            }
        }

        if (sessionPercent is null && weeklyPercent is null)
        {
            return null;
        }

        return new ClaudeLiveUsage
        {
            SessionPercent = sessionPercent,
            SessionResetsAt = sessionResetsAt,
            WeeklyPercent = weeklyPercent,
            WeeklyResetsAt = weeklyResetsAt,
            ScopedLimits = scopedLimits,
        };
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop)
        && prop.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _webView.Dispose();
        _window.Close();
    }
}
