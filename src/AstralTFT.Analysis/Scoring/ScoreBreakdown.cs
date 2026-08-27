namespace AstralTFT.Analysis.Scoring;

public sealed record ScoreComponent(
    string Key,
    double RawValue,
    double Weight,
    double Contribution,
    string? Explanation = null);

public sealed record ScoreBreakdown(
    string CandidateId,
    double Score,
    double Confidence,
    IReadOnlyList<ScoreComponent> Components);
