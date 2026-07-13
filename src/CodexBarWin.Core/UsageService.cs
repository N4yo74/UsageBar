using CodexBarWin.Core.Models;
using CodexBarWin.Core.Readers;

namespace CodexBarWin.Core;

/// <summary>
/// Entry point for UI layers (CLI today, WPF later). Reads local logs only -
/// never touches auth.json, Claude Desktop's cookies, or any other credential
/// file. Returns Codex (rate_limits) usage and Claude Code transcript-based
/// token/cost roll-ups only; exact Claude % usage (session/weekly/scoped) is
/// left unset here and, in the WPF app, is filled in by the App layer via its
/// own claude.ai WebView2 session (see CodexBarWin.App.ClaudeWebViewService).
/// </summary>
public sealed class UsageService
{
    private readonly CodexUsageReader _codexReader;
    private readonly ClaudeUsageReader _claudeReader;

    public UsageService()
        : this(new CodexUsageReader(), new ClaudeUsageReader())
    {
    }

    public UsageService(CodexUsageReader codexReader, ClaudeUsageReader claudeReader)
    {
        _codexReader = codexReader;
        _claudeReader = claudeReader;
    }

    public UsageSnapshot GetSnapshot()
    {
        var now = DateTimeOffset.Now;

        CodexUsage codex;
        try
        {
            codex = _codexReader.Read(now);
        }
        catch
        {
            codex = new CodexUsage { HasData = false };
        }

        // Transcript-based token/cost roll-up - the only Claude data this layer
        // knows how to produce. PercentSource stays Unavailable/percent fields
        // stay null; callers that have a live claude.ai session (the WPF app's
        // WebView2 service) layer the exact percentages on top themselves.
        ClaudeUsage claude;
        try
        {
            claude = _claudeReader.Read(now);
        }
        catch
        {
            claude = new ClaudeUsage { HasData = false };
        }

        return new UsageSnapshot
        {
            GeneratedAt = now,
            Codex = codex,
            Claude = claude,
        };
    }
}
