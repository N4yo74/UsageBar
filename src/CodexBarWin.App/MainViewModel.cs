using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using CodexBarWin.Core;
using CodexBarWin.Core.Models;
using Brush = System.Windows.Media.Brush;

namespace CodexBarWin.App;

/// <summary>
/// Backs the flyout UI. Pulls data from <see cref="UsageService"/> (file IO) on a
/// background thread and marshals results back to the UI thread via the Dispatcher.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush GoodBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#4CAF50")!;
    private static readonly SolidColorBrush WarnBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFC107")!;
    private static readonly SolidColorBrush BadBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#F44336")!;

    private readonly UsageService _usageService;
    private readonly ClaudeWebViewService _claudeWebViewService;
    private bool _autoShownLoginOnce;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(UsageService usageService, ClaudeWebViewService claudeWebViewService)
    {
        _usageService = usageService;
        _claudeWebViewService = claudeWebViewService;

        SelectCodexTabCommand = new RelayCommand(() => IsCodexTabSelected = true);
        SelectClaudeTabCommand = new RelayCommand(() => IsClaudeTabSelected = true);
        RefreshNowCommand = new RelayCommand(Refresh);
        ExitCommand = new RelayCommand(() => System.Windows.Application.Current.Shutdown());

        // Seed with placeholder text until the first refresh completes.
        StatusText = "読み込み中...";
        TooltipText = "CodexBarWin - 読み込み中...";
    }

    /// <summary>Raised whenever the tooltip text changes, so the tray icon can pick it up.</summary>
    public event EventHandler? TooltipChanged;

    public RelayCommand SelectCodexTabCommand { get; }
    public RelayCommand SelectClaudeTabCommand { get; }
    public RelayCommand RefreshNowCommand { get; }
    public RelayCommand ExitCommand { get; }

    private bool _isCodexTabSelected = true;
    public bool IsCodexTabSelected
    {
        get => _isCodexTabSelected;
        set
        {
            if (SetProperty(ref _isCodexTabSelected, value) && value)
            {
                IsClaudeTabSelected = false;
            }
        }
    }

    private bool _isClaudeTabSelected;
    public bool IsClaudeTabSelected
    {
        get => _isClaudeTabSelected;
        set
        {
            if (SetProperty(ref _isClaudeTabSelected, value) && value)
            {
                IsCodexTabSelected = false;
            }
        }
    }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private string _tooltipText = string.Empty;
    public string TooltipText
    {
        get => _tooltipText;
        private set
        {
            if (SetProperty(ref _tooltipText, value))
            {
                TooltipChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ---- Codex ----

    private bool _codexHasData;
    public bool CodexHasData { get => _codexHasData; private set => SetProperty(ref _codexHasData, value); }

    private string _codexPlanText = "";
    public string CodexPlanText { get => _codexPlanText; private set => SetProperty(ref _codexPlanText, value); }

    private double _codexSessionUsedPercent;
    public double CodexSessionUsedPercent { get => _codexSessionUsedPercent; private set => SetProperty(ref _codexSessionUsedPercent, value); }

    private string _codexSessionRemainingText = "n/a";
    public string CodexSessionRemainingText { get => _codexSessionRemainingText; private set => SetProperty(ref _codexSessionRemainingText, value); }

    private string _codexSessionResetText = "";
    public string CodexSessionResetText { get => _codexSessionResetText; private set => SetProperty(ref _codexSessionResetText, value); }

    private Brush _codexSessionBarBrush = GoodBrush;
    public Brush CodexSessionBarBrush { get => _codexSessionBarBrush; private set => SetProperty(ref _codexSessionBarBrush, value); }

    private double _codexWeeklyUsedPercent;
    public double CodexWeeklyUsedPercent { get => _codexWeeklyUsedPercent; private set => SetProperty(ref _codexWeeklyUsedPercent, value); }

    private string _codexWeeklyRemainingText = "n/a";
    public string CodexWeeklyRemainingText { get => _codexWeeklyRemainingText; private set => SetProperty(ref _codexWeeklyRemainingText, value); }

    private string _codexWeeklyResetText = "";
    public string CodexWeeklyResetText { get => _codexWeeklyResetText; private set => SetProperty(ref _codexWeeklyResetText, value); }

    private Brush _codexWeeklyBarBrush = GoodBrush;
    public Brush CodexWeeklyBarBrush { get => _codexWeeklyBarBrush; private set => SetProperty(ref _codexWeeklyBarBrush, value); }

    private string _codexTokensTodayText = "0";
    public string CodexTokensTodayText { get => _codexTokensTodayText; private set => SetProperty(ref _codexTokensTodayText, value); }

    private string _codexTokens7dText = "0";
    public string CodexTokens7dText { get => _codexTokens7dText; private set => SetProperty(ref _codexTokens7dText, value); }

    private string _codexTokens30dText = "0";
    public string CodexTokens30dText { get => _codexTokens30dText; private set => SetProperty(ref _codexTokens30dText, value); }

    // ---- Claude ----

    private bool _claudeHasData;
    public bool ClaudeHasData { get => _claudeHasData; private set => SetProperty(ref _claudeHasData, value); }

    private string _claudeLast5HoursText = "";
    public string ClaudeLast5HoursText { get => _claudeLast5HoursText; private set => SetProperty(ref _claudeLast5HoursText, value); }

    private string _claudeTodayText = "";
    public string ClaudeTodayText { get => _claudeTodayText; private set => SetProperty(ref _claudeTodayText, value); }

    private string _claudeLast7DaysText = "";
    public string ClaudeLast7DaysText { get => _claudeLast7DaysText; private set => SetProperty(ref _claudeLast7DaysText, value); }

    private string _claudeLast30DaysText = "";
    public string ClaudeLast30DaysText { get => _claudeLast30DaysText; private set => SetProperty(ref _claudeLast30DaysText, value); }

    // Exact percent-based usage, from claude.ai's own API (Claude Desktop's session cookies).
    // Only populated/shown when PercentSource == Api; otherwise we fall back to the
    // token/cost windows above.

    private bool _claudePercentAvailable;
    public bool ClaudePercentAvailable { get => _claudePercentAvailable; private set => SetProperty(ref _claudePercentAvailable, value); }

    private double _claudeSessionUsedPercent;
    public double ClaudeSessionUsedPercent { get => _claudeSessionUsedPercent; private set => SetProperty(ref _claudeSessionUsedPercent, value); }

    private string _claudeSessionPercentText = "";
    public string ClaudeSessionPercentText { get => _claudeSessionPercentText; private set => SetProperty(ref _claudeSessionPercentText, value); }

    private string _claudeSessionResetText = "";
    public string ClaudeSessionResetText { get => _claudeSessionResetText; private set => SetProperty(ref _claudeSessionResetText, value); }

    private Brush _claudeSessionBarBrush = GoodBrush;
    public Brush ClaudeSessionBarBrush { get => _claudeSessionBarBrush; private set => SetProperty(ref _claudeSessionBarBrush, value); }

    private double _claudeWeeklyUsedPercent;
    public double ClaudeWeeklyUsedPercent { get => _claudeWeeklyUsedPercent; private set => SetProperty(ref _claudeWeeklyUsedPercent, value); }

    private string _claudeWeeklyPercentText = "";
    public string ClaudeWeeklyPercentText { get => _claudeWeeklyPercentText; private set => SetProperty(ref _claudeWeeklyPercentText, value); }

    private string _claudeWeeklyResetText = "";
    public string ClaudeWeeklyResetText { get => _claudeWeeklyResetText; private set => SetProperty(ref _claudeWeeklyResetText, value); }

    private Brush _claudeWeeklyBarBrush = GoodBrush;
    public Brush ClaudeWeeklyBarBrush { get => _claudeWeeklyBarBrush; private set => SetProperty(ref _claudeWeeklyBarBrush, value); }

    public ObservableCollection<ScopedLimitDisplay> ClaudeScopedLimits { get; } = new();

    private string _claudeFallbackNoteText = "※claude.aiログインで正確な%を表示できます";
    public string ClaudeFallbackNoteText { get => _claudeFallbackNoteText; private set => SetProperty(ref _claudeFallbackNoteText, value); }

    /// <summary>
    /// Runs UsageService.GetSnapshot() on a background thread (it does file IO), then -
    /// back on the UI thread, where the WebView2 control lives - asks
    /// <see cref="ClaudeWebViewService"/> for the exact claude.ai percent usage and layers
    /// it on top. Never throws; a WebView2 failure just leaves the token/cost roll-up
    /// showing (PercentSource stays Unavailable).
    /// </summary>
    public async void Refresh()
    {
        UsageSnapshot? snapshot = null;
        Exception? error = null;
        try
        {
            snapshot = await Task.Run(() => _usageService.GetSnapshot());
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (snapshot is null)
        {
            ApplyError(error);
            return;
        }

        ClaudeLiveUsage? live = null;
        string? fetchError = null;
        try
        {
            live = await _claudeWebViewService.FetchAsync();
            fetchError = _claudeWebViewService.LastFetchError;
        }
        catch (Exception ex)
        {
            fetchError = $"other:{ex.GetType().Name}";
        }

        if (live is not null)
        {
            snapshot = snapshot with
            {
                Claude = snapshot.Claude with
                {
                    SessionPercent = live.SessionPercent,
                    SessionResetsAt = live.SessionResetsAt,
                    WeeklyPercent = live.WeeklyPercent,
                    WeeklyResetsAt = live.WeeklyResetsAt,
                    ScopedLimits = live.ScopedLimits,
                    PercentSource = ClaudePercentSource.Api,
                },
            };

            // A successful fetch means we're logged in - if the login window is
            // still on-screen (user just signed in), tuck it back away.
            if (_claudeWebViewService.IsLoginWindowVisible)
            {
                _claudeWebViewService.HideLoginWindow();
            }
        }
        else if (!_autoShownLoginOnce && fetchError is "not_logged_in" or "unauthorized")
        {
            // First time we notice there's no valid claude.ai session this run -
            // surface the login window once so the user can sign in. After that,
            // they can still reach it via the tray context menu.
            _autoShownLoginOnce = true;
            _claudeWebViewService.ShowLoginWindow();
        }

        ApplySnapshot(snapshot);
        DiagnosticsWriter.Write(snapshot.Claude, fetchError);
        DiagnosticsWriter.WriteBudget(snapshot);
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        ApplyCodex(snapshot.Codex);
        ApplyClaude(snapshot.Claude);

        StatusText = $"更新: {snapshot.GeneratedAt:HH:mm:ss}";
        TooltipText = Truncate(BuildTooltip(snapshot), 120);
    }

    private void ApplyError(Exception? error)
    {
        StatusText = "取得失敗" + (error is null ? "" : $" ({error.GetType().Name})");
        // Keep whatever was last displayed; just reflect the failure in the tooltip/status.
        if (string.IsNullOrEmpty(TooltipText) || TooltipText.StartsWith("CodexBarWin"))
        {
            TooltipText = "CodexBarWin - 取得失敗";
        }
    }

    private void ApplyCodex(CodexUsage codex)
    {
        CodexHasData = codex.HasData;
        CodexPlanText = codex.HasData ? $"Plan: {codex.PlanType ?? "unknown"}" : "";

        CodexSessionUsedPercent = ClampPercent(codex.SessionPercent);
        CodexSessionRemainingText = FormatRemaining(codex.SessionPercent);
        CodexSessionResetText = FormatResetIn(codex.SessionResetsAt);
        CodexSessionBarBrush = BrushForUsedPercent(CodexSessionUsedPercent);

        CodexWeeklyUsedPercent = ClampPercent(codex.WeeklyPercent);
        CodexWeeklyRemainingText = FormatRemaining(codex.WeeklyPercent);
        CodexWeeklyResetText = FormatResetIn(codex.WeeklyResetsAt);
        CodexWeeklyBarBrush = BrushForUsedPercent(CodexWeeklyUsedPercent);

        CodexTokensTodayText = codex.TokensToday.ToString("N0");
        CodexTokens7dText = codex.Tokens7d.ToString("N0");
        CodexTokens30dText = codex.Tokens30d.ToString("N0");
    }

    private void ApplyClaude(ClaudeUsage claude)
    {
        ClaudeHasData = claude.HasData;
        ClaudeLast5HoursText = FormatWindow(claude.Last5Hours);
        ClaudeTodayText = FormatWindow(claude.Today);
        ClaudeLast7DaysText = FormatWindow(claude.Last7Days);
        ClaudeLast30DaysText = FormatWindow(claude.Last30Days);

        ClaudePercentAvailable = claude.PercentSource == ClaudePercentSource.Api;

        if (ClaudePercentAvailable)
        {
            ClaudeSessionUsedPercent = ClampPercent(claude.SessionPercent);
            ClaudeSessionPercentText = $"{ClaudeSessionUsedPercent:F0}% 使用";
            ClaudeSessionResetText = FormatResetIn(claude.SessionResetsAt);
            ClaudeSessionBarBrush = BrushForUsedPercent(ClaudeSessionUsedPercent);

            ClaudeWeeklyUsedPercent = ClampPercent(claude.WeeklyPercent);
            ClaudeWeeklyPercentText = $"{ClaudeWeeklyUsedPercent:F0}% 使用";
            ClaudeWeeklyResetText = FormatResetIn(claude.WeeklyResetsAt);
            ClaudeWeeklyBarBrush = BrushForUsedPercent(ClaudeWeeklyUsedPercent);

            ClaudeScopedLimits.Clear();
            foreach (var scoped in claude.ScopedLimits)
            {
                var resetSuffix = scoped.ResetsAt.HasValue ? $"  ({FormatResetIn(scoped.ResetsAt)})" : "";
                ClaudeScopedLimits.Add(new ScopedLimitDisplay
                {
                    NameText = $"週間 ({scoped.DisplayName})",
                    PercentText = $"{scoped.Percent}%{resetSuffix}",
                });
            }

            ClaudeFallbackNoteText = "";
        }
        else
        {
            ClaudeScopedLimits.Clear();
            ClaudeFallbackNoteText = "※claude.aiログインで正確な%を表示できます";
        }
    }

    private static string FormatWindow(TokenUsageStats stats) =>
        $"{stats.TotalTokens:N0} tokens ・ ${stats.EstimatedCostUsd:F2}";

    private static double ClampPercent(double? percent) => percent.HasValue ? Math.Clamp(percent.Value, 0, 100) : 0;

    private static double ClampPercent(int? percent) => percent.HasValue ? Math.Clamp(percent.Value, 0, 100) : 0;

    private static string FormatRemaining(double? usedPercent) =>
        usedPercent.HasValue ? $"{Math.Clamp(100 - usedPercent.Value, 0, 100):F0}% 残り" : "データなし";

    private static string FormatResetIn(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return "リセット時刻不明";
        }

        var remaining = resetsAt.Value - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var hours = (int)remaining.TotalHours;
        var minutes = remaining.Minutes;
        return $"あと {hours}h {minutes}m でリセット";
    }

    private static Brush BrushForUsedPercent(double usedPercent)
    {
        if (usedPercent >= 80)
        {
            return BadBrush;
        }

        return usedPercent >= 50 ? WarnBrush : GoodBrush;
    }

    private static string BuildTooltip(UsageSnapshot snapshot)
    {
        var codex = snapshot.Codex;
        var claude = snapshot.Claude;

        var codexPart = codex.HasData && codex.SessionPercent.HasValue
            ? $"Codex {Math.Clamp(100 - codex.SessionPercent.Value, 0, 100):F0}%残"
            : "Codex -";

        var claudePart = claude.PercentSource == ClaudePercentSource.Api && claude.SessionPercent.HasValue
            ? $"Claude {Math.Clamp(100 - claude.SessionPercent.Value, 0, 100)}%残"
            : claude.HasData
                ? $"Claude ${claude.Today.EstimatedCostUsd:F2} 今日"
                : "Claude -";

        return $"CodexBarWin - {codexPart} ・ {claudePart}";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
