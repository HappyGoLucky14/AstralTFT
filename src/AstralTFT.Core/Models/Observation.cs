namespace AstralTFT.Core.Models;

public sealed record Observation<T>(
    T Value,
    Confidence Confidence,
    string Source,
    DateTimeOffset ObservedAt,
    string? RegionId = null,
    string? EvidenceHash = null);
