namespace CodexBarWin.Core.Models;

/// <summary>
/// Where <see cref="ClaudeUsage"/>'s percent fields came from.
/// </summary>
public enum ClaudePercentSource
{
    /// <summary>The claude.ai internal API could not be reached (no cookies, decrypt
    /// failure, HTTP error, etc). Percent fields are unset; only token/cost roll-ups
    /// from local transcripts are available.</summary>
    Unavailable,

    /// <summary>Percent fields came from claude.ai's own usage API (exact numbers).</summary>
    Api,
}
