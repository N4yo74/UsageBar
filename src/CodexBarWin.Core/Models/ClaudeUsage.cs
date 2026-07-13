namespace CodexBarWin.Core.Models;

/// <summary>
/// Snapshot of Claude Code usage across several rolling windows. Claude Code
/// does not expose a remaining-percent quota locally, so we roll up tokens
/// and an estimated cost instead.
/// </summary>
public sealed record ClaudeUsage
{
    /// <summary>True if at least one usage-bearing assistant message was found.</summary>
    public bool HasData { get; init; }

    public TokenUsageStats Last5Hours { get; init; } = new();
    public TokenUsageStats Today { get; init; } = new();
    public TokenUsageStats Last7Days { get; init; } = new();
    public TokenUsageStats Last30Days { get; init; } = new();

    // ---- Exact percent-based usage, fetched from claude.ai's own API when
    // available (see ClaudeApiUsageReader). Null/empty when PercentSource is
    // Unavailable - callers should fall back to the token/cost windows above.

    public int? SessionPercent { get; init; }
    public DateTimeOffset? SessionResetsAt { get; init; }

    public int? WeeklyPercent { get; init; }
    public DateTimeOffset? WeeklyResetsAt { get; init; }

    public List<ScopedLimit> ScopedLimits { get; init; } = new();

    public ClaudePercentSource PercentSource { get; init; } = ClaudePercentSource.Unavailable;
}
