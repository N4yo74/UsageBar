using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexBarWin.Core.Models;

namespace CodexBarWin.App;

/// <summary>
/// Writes a small, secret-free JSON snapshot of the most recent Claude refresh to
/// %LOCALAPPDATA%\CodexBarWin\diagnostics.json after every refresh, so refresh
/// success/failure and the resulting numbers can be checked without touching any
/// UI (or reading logs that might contain cookies/tokens - this file never does).
/// </summary>
public static class DiagnosticsWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static void Write(ClaudeUsage claude, string? fetchError)
    {
        try
        {
            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var dto = new DiagnosticsDto
            {
                Ts = DateTimeOffset.Now.ToString("O"),
                ClaudeSource = claude.PercentSource.ToString(),
                SessionPercent = claude.SessionPercent,
                SessionResetsAt = claude.SessionResetsAt?.ToString("O"),
                WeeklyPercent = claude.WeeklyPercent,
                WeeklyResetsAt = claude.WeeklyResetsAt?.ToString("O"),
                Scoped = claude.ScopedLimits
                    .Select(s => new DiagnosticsScopedDto { Name = s.DisplayName, Percent = s.Percent })
                    .ToList(),
                FetchError = fetchError,
            };

            File.WriteAllText(path, JsonSerializer.Serialize(dto, SerializerOptions));
        }
        catch
        {
            // Diagnostics are best-effort only - never let this take down a refresh.
        }
    }

    private static readonly JsonSerializerOptions BudgetOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes %LOCALAPPDATA%\CodexBarWin\budget.json - a compact, always-fresh, secret-free
    /// snapshot of remaining Codex/Claude budget that any tool (including a Claude Code
    /// session) can read in one cheap file read to decide how heavy a task to run.
    /// </summary>
    public static void WriteBudget(UsageSnapshot snapshot)
    {
        try
        {
            var path = GetBudgetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var codex = snapshot.Codex;
            var claude = snapshot.Claude;
            bool claudeApi = claude.PercentSource == ClaudePercentSource.Api;

            int? codexSess = Remaining(codex.SessionPercent);
            int? codexWeek = Remaining(codex.WeeklyPercent);
            int? claudeSess = claudeApi ? Remaining(claude.SessionPercent) : null;
            int? claudeWeek = claudeApi ? Remaining(claude.WeeklyPercent) : null;

            var sessions = new List<int>();
            if (codexSess.HasValue) sessions.Add(codexSess.Value);
            if (claudeSess.HasValue) sessions.Add(claudeSess.Value);
            int? minSession = sessions.Count > 0 ? sessions.Min() : null;
            string level = minSession is null ? "unknown"
                : minSession >= 40 ? "ample"
                : minSession >= 15 ? "moderate"
                : minSession >= 5 ? "tight"
                : "critical";
            string note = level switch
            {
                "critical" => "セッション枠がほぼ枯渇。重いサブエージェント/並列実行は避け、小さく分割するかリセットを待つ。",
                "tight" => "セッション枠が少ない。並列サブエージェントは1本に絞り、スコープを縮小する。",
                "moderate" => "セッション枠は中程度。大きな並列実行は控えめに。",
                "ample" => "セッション枠に余裕あり。通常通り実行してよい。",
                _ => "残量不明（未ログイン等）。念のため控えめに。",
            };

            var dto = new
            {
                ts = DateTimeOffset.Now.ToString("O"),
                codex = new
                {
                    hasData = codex.HasData,
                    plan = codex.PlanType,
                    sessionRemainingPercent = codexSess,
                    sessionResetsAt = codex.SessionResetsAt?.ToString("O"),
                    sessionResetsInText = ResetIn(codex.SessionResetsAt),
                    weeklyRemainingPercent = codexWeek,
                    weeklyResetsAt = codex.WeeklyResetsAt?.ToString("O"),
                    weeklyResetsInText = ResetIn(codex.WeeklyResetsAt),
                },
                claude = new
                {
                    source = claude.PercentSource.ToString(),
                    sessionRemainingPercent = claudeSess,
                    sessionResetsAt = claudeApi ? claude.SessionResetsAt?.ToString("O") : null,
                    sessionResetsInText = claudeApi ? ResetIn(claude.SessionResetsAt) : "",
                    weeklyRemainingPercent = claudeWeek,
                    weeklyResetsAt = claudeApi ? claude.WeeklyResetsAt?.ToString("O") : null,
                    weeklyResetsInText = claudeApi ? ResetIn(claude.WeeklyResetsAt) : "",
                    scoped = claudeApi
                        ? claude.ScopedLimits.Select(s => new { name = s.DisplayName, remainingPercent = Math.Clamp(100 - s.Percent, 0, 100) }).ToList()
                        : new(),
                },
                advice = new
                {
                    minSessionRemainingPercent = minSession,
                    level,
                    note,
                },
            };

            File.WriteAllText(path, JsonSerializer.Serialize(dto, BudgetOptions));
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static int? Remaining(double? usedPercent) =>
        usedPercent.HasValue ? (int)Math.Round(Math.Clamp(100 - usedPercent.Value, 0, 100)) : null;

    private static int? Remaining(int? usedPercent) =>
        usedPercent.HasValue ? Math.Clamp(100 - usedPercent.Value, 0, 100) : null;

    private static string ResetIn(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null) return "";
        var d = resetsAt.Value - DateTimeOffset.Now;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return $"あと {(int)d.TotalHours}h {d.Minutes}m";
    }

    private static string GetBudgetPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexBarWin", "budget.json");

    private static string GetPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexBarWin", "diagnostics.json");

    private sealed class DiagnosticsDto
    {
        [JsonPropertyName("ts")]
        public string Ts { get; init; } = "";

        [JsonPropertyName("claudeSource")]
        public string ClaudeSource { get; init; } = "";

        [JsonPropertyName("sessionPercent")]
        public int? SessionPercent { get; init; }

        [JsonPropertyName("sessionResetsAt")]
        public string? SessionResetsAt { get; init; }

        [JsonPropertyName("weeklyPercent")]
        public int? WeeklyPercent { get; init; }

        [JsonPropertyName("weeklyResetsAt")]
        public string? WeeklyResetsAt { get; init; }

        [JsonPropertyName("scoped")]
        public List<DiagnosticsScopedDto> Scoped { get; init; } = new();

        [JsonPropertyName("fetchError")]
        public string? FetchError { get; init; }
    }

    private sealed class DiagnosticsScopedDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("percent")]
        public int Percent { get; init; }
    }
}
