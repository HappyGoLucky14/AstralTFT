using AstralTFT.Core.Models;

namespace AstralTFT.Analysis.Personalization;

public sealed record ArchetypePerformance(
    CompArchetype Archetype,
    int Games,
    double AveragePlacement,
    double TopFourRate,
    double WinRate,
    double PlacementDeltaVsExpected,
    double Confidence);

public sealed record PlayerPlaystyleProfile(
    DateTimeOffset GeneratedAt,
    int GamesAnalyzed,
    IReadOnlyList<ArchetypePerformance> Archetypes,
    double EarlySlamRate,
    double AverageLevelEightStage,
    double RerollGameRate,
    double FastEightOrNineRate,
    double ProfileConfidence);

public sealed record PersonalizationWeight(
    double Weight,
    double Confidence,
    string Reason);

/// <summary>
/// Keeps personal history useful without allowing a small or stale sample to overpower current
/// patch/global evidence. The intended result is a modest adjustment, not a private tier list.
/// </summary>
public static class PersonalizationWeightPolicy
{
    public static PersonalizationWeight Calculate(
        int gamesAnalyzed,
        double profileReliability,
        double recentStability,
        double maximumWeight = 0.22)
    {
        var sampleMaturity = 1.0 - Math.Exp(-Math.Max(0, gamesAnalyzed) / 45.0);
        var reliability = Math.Clamp(profileReliability, 0, 1);
        var stability = Math.Clamp(recentStability, 0, 1);
        var confidence = Math.Clamp(sampleMaturity * reliability * (.65 + .35 * stability), 0, 1);
        var weight = Math.Clamp(maximumWeight, 0, .35) * confidence;

        return new PersonalizationWeight(
            weight,
            confidence,
            $"Personal sample {gamesAnalyzed} game(s), reliability {reliability:P0}, recent stability {stability:P0}.");
    }
}

public static class PersonalFitScorer
{
    public static double Score(CompArchetype candidate, PlayerPlaystyleProfile profile)
    {
        var matching = profile.Archetypes
            .Where(x => x.Archetype != CompArchetype.Unknown && candidate.HasFlag(x.Archetype))
            .ToArray();

        if (matching.Length == 0) return 0;

        // Negative placement delta means the player outperformed expectation. Convert that into a
        // bounded positive fit adjustment while still accounting for top-four/win conversion.
        var weighted = matching.Select(x =>
        {
            var sample = 1.0 - Math.Exp(-Math.Max(0, x.Games) / 20.0);
            var confidence = Math.Clamp(x.Confidence, 0, 1) * sample;
            var outperformance = Math.Clamp(-x.PlacementDeltaVsExpected / 0.50, -1, 1);
            var conversion = Math.Clamp(((x.TopFourRate - .50) * 1.2) + ((x.WinRate - .125) * .8), -.5, .5);
            return (score: Math.Clamp(outperformance * .7 + conversion * .3, -1, 1), weight: confidence);
        }).ToArray();

        var total = weighted.Sum(x => x.weight);
        if (total <= 1e-9) return 0;
        return Math.Clamp(weighted.Sum(x => x.score * x.weight) / total, -1, 1);
    }
}
