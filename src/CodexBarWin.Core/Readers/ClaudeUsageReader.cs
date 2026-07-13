using System.Globalization;
using System.Text.Json;
using CodexBarWin.Core.Internal;
using CodexBarWin.Core.Models;
using CodexBarWin.Core.Pricing;

namespace CodexBarWin.Core.Readers;

/// <summary>
/// Reads Claude Code transcript logs (%USERPROFILE%\.claude\projects\**\*.jsonl)
/// and rolls up token usage / estimated cost across a few time windows.
/// Claude Code has no local "percent remaining" concept, unlike Codex.
/// </summary>
public sealed class ClaudeUsageReader
{
    private readonly string _projectsRoot;

    public ClaudeUsageReader(string? projectsRoot = null)
    {
        _projectsRoot = projectsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "projects");
    }

    public ClaudeUsage Read(DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(effectiveNow.LocalDateTime);
        var fiveHoursAgo = effectiveNow - TimeSpan.FromHours(5);
        var sevenDaysAgo = effectiveNow - TimeSpan.FromDays(7);
        var thirtyDaysAgo = effectiveNow - TimeSpan.FromDays(30);

        if (!Directory.Exists(_projectsRoot))
        {
            return new ClaudeUsage { HasData = false };
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_projectsRoot, "*.jsonl", SearchOption.AllDirectories);
        }
        catch
        {
            return new ClaudeUsage { HasData = false };
        }

        var seenMessageIds = new HashSet<string>();

        var acc5h = new TokenAccumulator();
        var accToday = new TokenAccumulator();
        var acc7d = new TokenAccumulator();
        var acc30d = new TokenAccumulator();

        var anyData = false;

        foreach (var file in files)
        {
            try
            {
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

                        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        if (!root.TryGetProperty("timestamp", out var tsProp) || tsProp.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        if (!DateTimeOffset.TryParse(
                                tsProp.GetString(),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal,
                                out var ts))
                        {
                            continue;
                        }

                        // Dedupe by message.id - the same assistant message can be
                        // written more than once across resumed/forked transcripts.
                        if (message.TryGetProperty("id", out var idProp)
                            && idProp.ValueKind == JsonValueKind.String)
                        {
                            var id = idProp.GetString();
                            if (id is not null && !seenMessageIds.Add(id))
                            {
                                continue;
                            }
                        }

                        string? model = message.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String
                            ? modelProp.GetString()
                            : null;

                        var input = GetLong(usage, "input_tokens");
                        var output = GetLong(usage, "output_tokens");
                        var cacheCreation = GetLong(usage, "cache_creation_input_tokens");
                        var cacheRead = GetLong(usage, "cache_read_input_tokens");

                        if (input == 0 && output == 0 && cacheCreation == 0 && cacheRead == 0)
                        {
                            continue;
                        }

                        anyData = true;

                        var cost = ModelPricing.EstimateCostUsd(model, input, output, cacheCreation, cacheRead);

                        if (ts >= fiveHoursAgo)
                        {
                            acc5h.Add(input, output, cacheCreation, cacheRead, cost);
                        }

                        if (DateOnly.FromDateTime(ts.LocalDateTime) == today)
                        {
                            accToday.Add(input, output, cacheCreation, cacheRead, cost);
                        }

                        if (ts >= sevenDaysAgo)
                        {
                            acc7d.Add(input, output, cacheCreation, cacheRead, cost);
                        }

                        if (ts >= thirtyDaysAgo)
                        {
                            acc30d.Add(input, output, cacheCreation, cacheRead, cost);
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

        return new ClaudeUsage
        {
            HasData = anyData,
            Last5Hours = acc5h.ToStats(),
            Today = accToday.ToStats(),
            Last7Days = acc7d.ToStats(),
            Last30Days = acc30d.ToStats(),
        };
    }

    private static long GetLong(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt64(out var value))
        {
            return value;
        }

        return 0;
    }

    private sealed class TokenAccumulator
    {
        private long _input;
        private long _output;
        private long _cacheCreation;
        private long _cacheRead;
        private decimal _cost;

        public void Add(long input, long output, long cacheCreation, long cacheRead, decimal cost)
        {
            _input += input;
            _output += output;
            _cacheCreation += cacheCreation;
            _cacheRead += cacheRead;
            _cost += cost;
        }

        public TokenUsageStats ToStats() => new()
        {
            InputTokens = _input,
            OutputTokens = _output,
            CacheCreationTokens = _cacheCreation,
            CacheReadTokens = _cacheRead,
            EstimatedCostUsd = _cost,
        };
    }
}
