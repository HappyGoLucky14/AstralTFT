using AstralTFT.Core.Models;

namespace AstralTFT.Analysis.Playstyles;

public sealed record CompPlaystyleResolution(
    CompPlaystyleProfile? Primary,
    IReadOnlyList<CompDirectionProbability> Directions,
    double Confidence,
    bool IsAmbiguous = false);

public static class CompPlaystyleResolver
{
    public static CompPlaystyleResolution Resolve(
        IEnumerable<CompDirectionProbability> directions,
        IReadOnlyDictionary<string, CompPlaystyleProfile> profiles,
        double primaryThreshold = 0.62,
        double minimumLead = 0.10)
    {
        var raw = directions
            .Where(x => x.Probability > 0 && x.Confidence > 0)
            .ToArray();

        if (raw.Length == 0)
            return new CompPlaystyleResolution(null, Array.Empty<CompDirectionProbability>(), 0, true);

        var total = raw.Sum(x => x.Probability);
        var normalized = raw
            .Select(x => x with { Probability = x.Probability / total })
            .OrderByDescending(x => x.Probability)
            .ToArray();

        var top = normalized[0];
        var runnerUp = normalized.Length > 1 ? normalized[1].Probability : 0;
        var lead = top.Probability - runnerUp;
        var ambiguous = top.Probability < primaryThreshold || lead < minimumLead;

        profiles.TryGetValue(top.CompId, out var profile);
        var primary = !ambiguous ? profile : null;
        var leadConfidence = Math.Clamp(lead / Math.Max(.01, minimumLead * 2.0), 0, 1);
        var confidence = Math.Clamp(top.Probability * top.Confidence * (.65 + .35 * leadConfidence), 0, 1);

        return new CompPlaystyleResolution(primary, normalized, confidence, ambiguous);
    }
}
