using System.Globalization;
using System.Text.Json;
using CodexBarWin.Core.Internal;
using CodexBarWin.Core.Models;

namespace CodexBarWin.Core.Readers;

/// <summary>
/// Reads Codex CLI rollout session logs (%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-*.jsonl)
/// and extracts the current rate-limit snapshot plus a rough token roll-up.
/// </summary>
public sealed class CodexUsageReader
{
    private readonly string _sessionsRoot;

    public CodexUsageReader(string? sessionsRoot = null)
    {
        _sessionsRoot = sessionsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");
    }

    public CodexUsage Read(DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(effectiveNow.LocalDateTime);

        if (!Directory.Exists(_sessionsRoot))
        {
            return new CodexUsage { HasData = false };
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories);
        }
        catch
        {
            return new CodexUsage { HasData = false };
        }

        DateTimeOffset? latestTimestamp = null;
        RateLimitSnapshot? latestRateLimits = null;

        long tokensToday = 0;
        long tokens7d = 0;
        long tokens30d = 0;

        foreach (var file in files)
        {
            try
            {
                long? lastSessionTotalTokens = null;

                foreach (var line in JsonLineReader.ReadLinesSafe(file))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(line);
                    }
                    catch
                    {
                        continue; // broken/partial line - skip it
                    }

                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        if (!payload.TryGetProperty("type", out var typeProp)
                            || typeProp.ValueKind != JsonValueKind.String
                            || typeProp.GetString() != "token_count")
                        {
                            continue;
                        }

                        if (payload.TryGetProperty("info", out var info)
                            && info.TryGetProperty("total_token_usage", out var totalUsage)
                            && totalUsage.TryGetProperty("total_tokens", out var totalTokensProp)
                            && totalTokensProp.ValueKind == JsonValueKind.Number
                            && totalTokensProp.TryGetInt64(out var totalTokens))
                        {
                            lastSessionTotalTokens = totalTokens;
                        }

                        if (payload.TryGetProperty("rate_limits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
                        {
                            DateTimeOffset? ts = null;
                            if (root.TryGetProperty("timestamp", out var tsProp) && tsProp.ValueKind == JsonValueKind.String)
                            {
                                if (DateTimeOffset.TryParse(
                                        tsProp.GetString(),
                                        CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal,
                                        out var parsedTs))
                                {
                                    ts = parsedTs;
                                }
                            }

                            if (ts.HasValue && (latestTimestamp is null || ts.Value > latestTimestamp.Value))
                            {
                                latestTimestamp = ts;
                                latestRateLimits = ParseRateLimits(rateLimits);
                            }
                        }
                    }
                }

                if (lastSessionTotalTokens.HasValue)
                {
                    var sessionDate = ExtractSessionDate(file) ?? SafeGetMtimeDate(file);
                    if (sessionDate.HasValue)
                    {
                        var daysAgo = today.DayNumber - sessionDate.Value.DayNumber;
                        if (daysAgo == 0)
                        {
                            tokensToday += lastSessionTotalTokens.Value;
                        }
                        if (daysAgo >= 0 && daysAgo < 7)
                        {
                            tokens7d += lastSessionTotalTokens.Value;
                        }
                        if (daysAgo >= 0 && daysAgo < 30)
                        {
                            tokens30d += lastSessionTotalTokens.Value;
                        }
                    }
                }
            }
            catch
            {
                // A whole file failed unexpectedly (permissions, IO, etc). Skip it, keep going.
                continue;
            }
        }

        if (latestRateLimits is null)
        {
            return new CodexUsage
            {
                HasData = false,
                TokensToday = tokensToday,
                Tokens7d = tokens7d,
                Tokens30d = tokens30d,
            };
        }

        DateTimeOffset? sessionResetsAt = latestRateLimits.PrimaryResetsAtUnix is long prUnix
            ? DateTimeOffset.FromUnixTimeSeconds(prUnix)
            : null;
        DateTimeOffset? weeklyResetsAt = latestRateLimits.SecondaryResetsAtUnix is long srUnix
            ? DateTimeOffset.FromUnixTimeSeconds(srUnix)
            : null;

        return new CodexUsage
        {
            HasData = true,
            PlanType = latestRateLimits.PlanType,
            LastUpdatedAt = latestTimestamp,

            SessionPercent = latestRateLimits.PrimaryUsedPercent,
            SessionWindowMinutes = latestRateLimits.PrimaryWindowMinutes,
            SessionResetsAt = sessionResetsAt,
            SessionResetsIn = ClampNonNegative(sessionResetsAt, effectiveNow),

            WeeklyPercent = latestRateLimits.SecondaryUsedPercent,
            WeeklyWindowMinutes = latestRateLimits.SecondaryWindowMinutes,
            WeeklyResetsAt = weeklyResetsAt,
            WeeklyResetsIn = ClampNonNegative(weeklyResetsAt, effectiveNow),

            TokensToday = tokensToday,
            Tokens7d = tokens7d,
            Tokens30d = tokens30d,
        };
    }

    private static TimeSpan? ClampNonNegative(DateTimeOffset? at, DateTimeOffset now)
    {
        if (!at.HasValue)
        {
            return null;
        }

        var delta = at.Value - now;
        return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
    }

    private static DateOnly? SafeGetMtimeDate(string file)
    {
        try
        {
            return DateOnly.FromDateTime(File.GetLastWriteTime(file));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extracts the YYYY/MM/DD segment from a ...\sessions\YYYY\MM\DD\rollout-*.jsonl path.</summary>
    private static DateOnly? ExtractSessionDate(string filePath)
    {
        var parts = filePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length - 3; i++)
        {
            if (string.Equals(parts[i], "sessions", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[i + 1], out var year)
                && int.TryParse(parts[i + 2], out var month)
                && int.TryParse(parts[i + 3], out var day))
            {
                try
                {
                    return new DateOnly(year, month, day);
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static RateLimitSnapshot ParseRateLimits(JsonElement rateLimits)
    {
        double? primaryPercent = null;
        int? primaryWindow = null;
        long? primaryResets = null;

        double? secondaryPercent = null;
        int? secondaryWindow = null;
        long? secondaryResets = null;

        string? planType = null;

        if (rateLimits.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.Object)
        {
            if (primary.TryGetProperty("used_percent", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                primaryPercent = p.GetDouble();
            }
            if (primary.TryGetProperty("window_minutes", out var w) && w.ValueKind == JsonValueKind.Number)
            {
                primaryWindow = w.GetInt32();
            }
            if (primary.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number)
            {
                primaryResets = r.GetInt64();
            }
        }

        if (rateLimits.TryGetProperty("secondary", out var secondary) && secondary.ValueKind == JsonValueKind.Object)
        {
            if (secondary.TryGetProperty("used_percent", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                secondaryPercent = p.GetDouble();
            }
            if (secondary.TryGetProperty("window_minutes", out var w) && w.ValueKind == JsonValueKind.Number)
            {
                secondaryWindow = w.GetInt32();
            }
            if (secondary.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number)
            {
                secondaryResets = r.GetInt64();
            }
        }

        if (rateLimits.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String)
        {
            planType = pt.GetString();
        }

        return new RateLimitSnapshot(
            primaryPercent, primaryWindow, primaryResets,
            secondaryPercent, secondaryWindow, secondaryResets,
            planType);
    }

    private sealed record RateLimitSnapshot(
        double? PrimaryUsedPercent,
        int? PrimaryWindowMinutes,
        long? PrimaryResetsAtUnix,
        double? SecondaryUsedPercent,
        int? SecondaryWindowMinutes,
        long? SecondaryResetsAtUnix,
        string? PlanType);
}
