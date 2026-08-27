using System.Text.RegularExpressions;
using AstralTFT.Core.Models;

namespace AstralTFT.Analysis.Coaching;

public enum ReviewAvailability
{
    Hidden = 0,
    RetrospectiveOnly = 1,
    PostGameDetailed = 2
}

public enum ReviewSeverity
{
    Info = 0,
    Opportunity = 1,
    Important = 2,
    Critical = 3
}

public sealed record ReviewEvidence(
    string Key,
    string Label,
    string Value,
    double Confidence);

public sealed record RoundReview(
    StagePoint Stage,
    string CompId,
    CompArchetype Archetype,
    string Headline,
    string Explanation,
    ReviewSeverity Severity,
    double Confidence,
    IReadOnlyList<ReviewEvidence> Evidence,
    ReviewAvailability Availability,
    string? Alternative = null);

/// <summary>
/// Prevents low-value generic filler from reaching the companion UI. Specific analysis must cite
/// concrete state evidence and a comp/line context. If the engine cannot clear this gate, silence is
/// preferable to "check positioning"-style noise.
/// </summary>
public static partial class RoundReviewSpecificityGate
{
    private static readonly string[] GenericLeadIns =
    [
        "check positioning",
        "consider positioning",
        "consider economy",
        "consider saving",
        "consider rolling",
        "maybe save",
        "maybe roll",
        "improve your board",
        "play strongest board"
    ];

    public static bool ShouldDisplay(RoundReview review, double minimumSpecificity = 0.62) =>
        SpecificityScore(review) >= minimumSpecificity;

    public static double SpecificityScore(RoundReview review)
    {
        if (review.Availability == ReviewAvailability.Hidden) return 0;
        if (review.Confidence < 0.65) return 0;
        if (string.IsNullOrWhiteSpace(review.CompId)) return 0;
        if (review.Archetype == CompArchetype.Unknown) return 0;
        if (string.IsNullOrWhiteSpace(review.Headline) || string.IsNullOrWhiteSpace(review.Explanation)) return 0;

        var headline = review.Headline.Trim();
        var lower = headline.ToLowerInvariant();
        if (GenericLeadIns.Any(x => lower.StartsWith(x, StringComparison.OrdinalIgnoreCase))) return 0;

        var evidence = review.Evidence
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToArray();
        if (evidence.Length < 2) return 0;

        var evidenceConfidence = evidence.Average(x => Math.Clamp(x.Confidence, 0, 1));
        var uniqueEvidence = evidence.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var evidenceBreadth = Math.Clamp(uniqueEvidence / 4.0, 0, 1);

        var concreteHeadline = NumberOrTftTokenRegex().IsMatch(headline) ? 1.0 : 0.60;
        var explanationDepth = Math.Clamp(review.Explanation.Length / 140.0, 0.35, 1.0);
        var alternativeSignal = string.IsNullOrWhiteSpace(review.Alternative) ? 0.55 : 1.0;

        return Math.Clamp(
            (Math.Clamp(review.Confidence, 0, 1) * .25) +
            (evidenceConfidence * .20) +
            (evidenceBreadth * .20) +
            (concreteHeadline * .20) +
            (explanationDepth * .10) +
            (alternativeSignal * .05),
            0,
            1);
    }

    [GeneratedRegex(@"(?:\d|★|\bg\b|\bhp\b|level\s*\d|stage\s*\d|\d-\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberOrTftTokenRegex();
}
