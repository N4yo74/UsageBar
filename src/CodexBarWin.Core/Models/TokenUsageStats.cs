namespace CodexBarWin.Core.Models;

/// <summary>
/// Aggregated token usage (and estimated cost) for a single time window.
/// </summary>
public sealed record TokenUsageStats
{
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheCreationTokens { get; init; }
    public long CacheReadTokens { get; init; }

    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;

    public decimal EstimatedCostUsd { get; init; }
}
