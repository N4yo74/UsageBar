namespace CodexBarWin.Core.Models;

/// <summary>
/// Top-level result returned by <see cref="UsageService.GetSnapshot"/>.
/// UI layers (CLI today, WPF later) should only ever read this.
/// </summary>
public sealed record UsageSnapshot
{
    public DateTimeOffset GeneratedAt { get; init; }
    public CodexUsage Codex { get; init; } = new();
    public ClaudeUsage Claude { get; init; } = new();
}
