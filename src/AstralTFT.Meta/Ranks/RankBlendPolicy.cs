namespace AstralTFT.Meta.Ranks;

public enum TftRankTier
{
    Iron = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4,
    Emerald = 5,
    Diamond = 6,
    Master = 7,
    Grandmaster = 8,
    Challenger = 9
}

public sealed record RankBucketCandidate(
    string Id,
    TftRankTier AnchorTier,
    long SampleSize,
    double SourceQuality,
    bool IncludesUserTier = false);

public sealed record RankBucketWeight(
    string Id,
    double Weight,
    string Reason);

/// <summary>
/// Blends the player's own skill context with higher-Elo signal instead of blindly using one
/// bracket. Sample maturity prevents tiny Challenger-only slices from dominating.
/// </summary>
public static class RankBlendPolicy
{
    public static IReadOnlyList<RankBucketWeight> Calculate(
        TftRankTier? userTier,
        IReadOnlyList<RankBucketCandidate> buckets)
    {
        if (buckets.Count == 0) return Array.Empty<RankBucketWeight>();

        var user = (int)(userTier ?? TftRankTier.Diamond);
        var raw = buckets.Select(bucket =>
        {
            var anchor = (int)bucket.AnchorTier;
            var distance = Math.Abs(anchor - user);

            // Higher-rank data loses relevance more slowly than lower-rank data because the product
            // explicitly values forward-looking/high-Elo strategy signal.
            var relevanceDecay = anchor >= user ? 0.18 : 0.32;
            var relevance = Math.Exp(-distance * relevanceDecay);
            if (bucket.IncludesUserTier) relevance *= 1.18;

            var sampleMaturity = 1.0 - Math.Exp(-Math.Max(0, bucket.SampleSize) / 8_000.0);
            var quality = Math.Clamp(bucket.SourceQuality, 0, 1);
            var highEloSignal = 0.88 + (0.12 * anchor / (int)TftRankTier.Challenger);
            var weight = relevance * sampleMaturity * quality * highEloSignal;
            return (bucket, weight);
        }).ToArray();

        var sum = raw.Sum(x => x.weight);
        if (sum <= 1e-9)
            return raw.Select(x => new RankBucketWeight(x.bucket.Id, 0, "No reliable sample weight.")).ToArray();

        return raw
            .Select(x => new RankBucketWeight(
                x.bucket.Id,
                x.weight / sum,
                $"Rank relevance to {(userTier?.ToString() ?? "unknown")}, sample {x.bucket.SampleSize:N0}, quality {x.bucket.SourceQuality:P0}."))
            .OrderByDescending(x => x.Weight)
            .ToArray();
    }
}
