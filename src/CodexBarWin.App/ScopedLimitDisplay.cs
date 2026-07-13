namespace CodexBarWin.App;

/// <summary>Pre-formatted row for one Claude "weekly_scoped" limit (e.g. a specific model).</summary>
public sealed class ScopedLimitDisplay
{
    public string NameText { get; init; } = "";
    public string PercentText { get; init; } = "";
}
