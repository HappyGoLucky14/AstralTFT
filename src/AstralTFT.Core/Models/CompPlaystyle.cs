namespace AstralTFT.Core.Models;

[Flags]
public enum CompArchetype
{
    Unknown = 0,
    OneCostReroll = 1 << 0,
    TwoCostReroll = 1 << 1,
    ThreeCostReroll = 1 << 2,
    Tempo = 1 << 3,
    FastEight = 1 << 4,
    FastNine = 1 << 5,
    Vertical = 1 << 6,
    Flex = 1 << 7,
    CappedBoard = 1 << 8,
    LossStreak = 1 << 9,
    WinStreak = 1 << 10
}

public sealed record RollWindow(
    int Level,
    StagePoint? EarliestStage,
    StagePoint? LatestStage,
    int PreferredGoldFloor,
    string Purpose);

public sealed record LevelTiming(
    int TargetLevel,
    StagePoint? TypicalStage,
    int? MinimumGoldAfterLevel,
    string Purpose);

public sealed record CompPlaystyleProfile(
    string CompId,
    string DisplayName,
    CompArchetype Archetypes,
    IReadOnlyList<RollWindow> RollWindows,
    IReadOnlyList<LevelTiming> LevelTimings,
    IReadOnlySet<string> RequiredThreeStarChampionIds,
    IReadOnlySet<string> OptionalThreeStarChampionIds,
    IReadOnlySet<string> PrimaryCarryIds,
    IReadOnlySet<string> PrimaryTankIds,
    IReadOnlySet<string> CriticalTraitIds,
    double ItemFlexibility,
    double TransitionFlexibility,
    string PrimaryWinCondition,
    string? Notes = null);

public sealed record CompDirectionProbability(
    string CompId,
    double Probability,
    double Confidence,
    string? Reason = null);
