namespace CodexBarWin.Core.Models;

/// <summary>
/// A single "weekly_scoped" entry from the claude.ai usage API - a per-model
/// weekly limit (e.g. a specific model showing 0% used).
/// </summary>
public sealed record ScopedLimit
{
    public string DisplayName { get; init; } = "";
    public int Percent { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
}
