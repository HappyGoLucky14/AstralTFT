namespace AstralTFT.Meta.Quality;

public sealed record SourceQualityInput(
    bool ExactPatch,
    TimeSpan Age,
    long? SampleSize,
    bool ExactRankFilter,
    bool ExactRegionFilter,
    double HistoricalReliability,
    double ConditionSpecificity);

public static class SourceQuality
{
    public static double Score(SourceQualityInput input)
    {
        var patch = input.ExactPatch ? 1.0 : 0.35;
        var freshness = Math.Exp(-Math.Max(0, input.Age.TotalHours) / 24.0);
        var sample = input.SampleSize switch
        {
            null => 0.45,
            <= 100 => 0.25,
            <= 1_000 => 0.55,
            <= 10_000 => 0.78,
            <= 100_000 => 0.92,
            _ => 1.0
        };
        var rank = input.ExactRankFilter ? 1.0 : 0.72;
        var region = input.ExactRegionFilter ? 1.0 : 0.88;
        var reliability = Math.Clamp(input.HistoricalReliability, 0, 1);
        var specificity = Math.Clamp(input.ConditionSpecificity, 0, 1);

        // Multiplicative core prevents a stale/wrong-patch giant sample from dominating.
        var core = Math.Pow(patch * freshness * sample, 1.0 / 3.0);
        var context = (rank * 0.35) + (region * 0.15) + (reliability * 0.30) + (specificity * 0.20);
        return Math.Clamp(core * context, 0, 1);
    }
}
