namespace AstralTFT.Meta.Sources;

public sealed record ActiveSetRequest(string PatchHint = "latest");

public sealed record ActiveSetInfo(
    string SetName,
    string CoreSetName,
    string DisplayName,
    string AugmentSystemName,
    DateTimeOffset ObservedAt);
