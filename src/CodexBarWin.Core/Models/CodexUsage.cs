namespace CodexBarWin.Core.Models;

/// <summary>
/// Snapshot of Codex CLI usage, derived from the most recent rate_limits event
/// found across all rollout session files, plus a rough token roll-up for
/// today / last 7 days / last 30 days.
/// </summary>
public sealed record CodexUsage
{
    /// <summary>True if at least one rate_limits event was found.</summary>
    public bool HasData { get; init; }

    public string? PlanType { get; init; }

    /// <summary>Timestamp (from the rollout log) of the rate_limits snapshot used below.</summary>
    public DateTimeOffset? LastUpdatedAt { get; init; }

    // Primary = short (5h-ish) session window.
    public double? SessionPercent { get; init; }
    public int? SessionWindowMinutes { get; init; }
    public DateTimeOffset? SessionResetsAt { get; init; }
    public TimeSpan? SessionResetsIn { get; init; }

    // Secondary = weekly window.
    public double? WeeklyPercent { get; init; }
    public int? WeeklyWindowMinutes { get; init; }
    public DateTimeOffset? WeeklyResetsAt { get; init; }
    public TimeSpan? WeeklyResetsIn { get; init; }

    public long TokensToday { get; init; }
    public long Tokens7d { get; init; }
    public long Tokens30d { get; init; }
}
