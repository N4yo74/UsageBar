namespace CodexBarWin.Core.Pricing;

/// <summary>Per-million-token USD pricing for a model family.</summary>
public sealed record ModelPrice(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal CacheWritePerMillion,
    decimal CacheReadPerMillion);

/// <summary>
/// Rough cost estimator keyed by substring match on the model id
/// ("opus" / "sonnet" / "haiku"). Numbers are approximate list prices and
/// are intentionally kept in one place so they're easy to update later.
/// </summary>
public static class ModelPricing
{
    private static readonly ModelPrice Opus = new(
        InputPerMillion: 15.00m,
        OutputPerMillion: 75.00m,
        CacheWritePerMillion: 18.75m,
        CacheReadPerMillion: 1.50m);

    private static readonly ModelPrice Sonnet = new(
        InputPerMillion: 3.00m,
        OutputPerMillion: 15.00m,
        CacheWritePerMillion: 3.75m,
        CacheReadPerMillion: 0.30m);

    private static readonly ModelPrice Haiku = new(
        InputPerMillion: 0.80m,
        OutputPerMillion: 4.00m,
        CacheWritePerMillion: 1.00m,
        CacheReadPerMillion: 0.08m);

    /// <summary>Resolve pricing for a model id. Unknown models fall back to Sonnet pricing.</summary>
    public static ModelPrice Resolve(string? modelId)
    {
        if (!string.IsNullOrEmpty(modelId))
        {
            var lower = modelId.ToLowerInvariant();
            if (lower.Contains("opus")) return Opus;
            if (lower.Contains("haiku")) return Haiku;
            if (lower.Contains("sonnet")) return Sonnet;
        }

        return Sonnet;
    }

    public static decimal EstimateCostUsd(
        string? modelId,
        long inputTokens,
        long outputTokens,
        long cacheCreationTokens,
        long cacheReadTokens)
    {
        var price = Resolve(modelId);

        return (inputTokens * price.InputPerMillion
              + outputTokens * price.OutputPerMillion
              + cacheCreationTokens * price.CacheWritePerMillion
              + cacheReadTokens * price.CacheReadPerMillion) / 1_000_000m;
    }
}
