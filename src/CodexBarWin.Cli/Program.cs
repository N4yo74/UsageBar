using CodexBarWin.Core;
using CodexBarWin.Core.Models;

var service = new UsageService();
var snapshot = service.GetSnapshot();

Console.WriteLine("======================================================");
Console.WriteLine(" CodexBarWin - Usage Summary");
Console.WriteLine($" generated at {snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
Console.WriteLine("======================================================");
Console.WriteLine();

PrintCodex(snapshot.Codex);
Console.WriteLine();
PrintClaude(snapshot.Claude);

return 0;

static void PrintCodex(CodexUsage codex)
{
    Console.WriteLine("[Codex]");

    if (!codex.HasData)
    {
        Console.WriteLine("  データが見つかりませんでした。");
        Console.WriteLine("  (%USERPROFILE%\\.codex\\sessions が無いか、rate_limits を含む行が見つかりません)");
        if (codex.TokensToday + codex.Tokens7d + codex.Tokens30d > 0)
        {
            Console.WriteLine($"  Tokens today/7d/30d : {codex.TokensToday:N0} / {codex.Tokens7d:N0} / {codex.Tokens30d:N0}");
        }
        return;
    }

    Console.WriteLine($"  Plan             : {codex.PlanType ?? "unknown"}");
    Console.WriteLine($"  Session (5h)     : {FormatPercent(codex.SessionPercent)}  resets {FormatResets(codex.SessionResetsAt, codex.SessionResetsIn)}");
    Console.WriteLine($"  Weekly           : {FormatPercent(codex.WeeklyPercent)}  resets {FormatResets(codex.WeeklyResetsAt, codex.WeeklyResetsIn)}");
    Console.WriteLine($"  Tokens today     : {codex.TokensToday:N0}");
    Console.WriteLine($"  Tokens 7d        : {codex.Tokens7d:N0}");
    Console.WriteLine($"  Tokens 30d       : {codex.Tokens30d:N0}");
    if (codex.LastUpdatedAt.HasValue)
    {
        Console.WriteLine($"  (as of           : {codex.LastUpdatedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} local)");
    }
}

static void PrintClaude(ClaudeUsage claude)
{
    Console.WriteLine("[Claude Code]");

    if (claude.PercentSource == ClaudePercentSource.Api)
    {
        Console.WriteLine("  Percent source   : Api (claude.ai)");
        Console.WriteLine($"  Session          : {FormatPercentInt(claude.SessionPercent)}  resets {FormatResetsAt(claude.SessionResetsAt)}");
        Console.WriteLine($"  Weekly (all)     : {FormatPercentInt(claude.WeeklyPercent)}  resets {FormatResetsAt(claude.WeeklyResetsAt)}");
        foreach (var scoped in claude.ScopedLimits)
        {
            Console.WriteLine($"  Weekly ({scoped.DisplayName,-8}): {FormatPercentInt(scoped.Percent)}  resets {FormatResetsAt(scoped.ResetsAt)}");
        }
    }
    else
    {
        Console.WriteLine("  Percent source   : Unavailable (transcript積算のみ - %表示はWPF版のClaude連携ログインが必要)");
    }

    Console.WriteLine();

    if (!claude.HasData)
    {
        Console.WriteLine("  データが見つかりませんでした。");
        Console.WriteLine("  (%USERPROFILE%\\.claude\\projects が無いか、usage を含む行が見つかりません)");
        return;
    }

    PrintWindow("Last 5 hours", claude.Last5Hours);
    PrintWindow("Today", claude.Today);
    PrintWindow("Last 7 days", claude.Last7Days);
    PrintWindow("Last 30 days", claude.Last30Days);
}

static void PrintWindow(string label, TokenUsageStats stats)
{
    Console.WriteLine(
        $"  {label,-12}: tokens={stats.TotalTokens,10:N0}  " +
        $"(in={stats.InputTokens:N0}, out={stats.OutputTokens:N0}, " +
        $"cache_w={stats.CacheCreationTokens:N0}, cache_r={stats.CacheReadTokens:N0})  " +
        $"cost=${stats.EstimatedCostUsd:F2}");
}

static string FormatPercent(double? percent) =>
    percent.HasValue ? $"{percent.Value,5:F1}%" : "  n/a";

static string FormatPercentInt(int? percent) =>
    percent.HasValue ? $"{percent.Value,3:D}%" : "n/a";

static string FormatResets(DateTimeOffset? at, TimeSpan? inSpan)
{
    if (!at.HasValue)
    {
        return "n/a";
    }

    var span = inSpan ?? TimeSpan.Zero;
    var totalHours = (long)span.TotalHours;
    return $"{at.Value.ToLocalTime():yyyy-MM-dd HH:mm} (in {totalHours}h {span.Minutes}m)";
}

static string FormatResetsAt(DateTimeOffset? at)
{
    if (!at.HasValue)
    {
        return "n/a";
    }

    var span = at.Value - DateTimeOffset.Now;
    if (span < TimeSpan.Zero)
    {
        span = TimeSpan.Zero;
    }

    return FormatResets(at, span);
}
