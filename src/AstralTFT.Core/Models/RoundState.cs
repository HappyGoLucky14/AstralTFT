namespace AstralTFT.Core.Models;

public enum RoundPhase
{
    Unknown = 0,
    Planning = 1,
    Combat = 2,
    Loot = 3,
    Carousel = 4,
    AugmentSelection = 5,
    PostCombat = 6
}

public readonly record struct StagePoint(int Stage, int Round) : IComparable<StagePoint>
{
    public static bool TryParse(string? value, out StagePoint stagePoint)
    {
        stagePoint = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var stage) || !int.TryParse(parts[1], out var round)) return false;
        if (stage < 1 || round < 0) return false;

        stagePoint = new StagePoint(stage, round);
        return true;
    }

    public int CompareTo(StagePoint other)
    {
        var stageComparison = Stage.CompareTo(other.Stage);
        return stageComparison != 0 ? stageComparison : Round.CompareTo(other.Round);
    }

    public override string ToString() => $"{Stage}-{Round}";
}

public sealed record RoundState(
    StagePoint? Stage,
    RoundPhase Phase,
    DateTimeOffset? PhaseStartedAt = null,
    bool IsPveRound = false,
    bool IsAugmentRound = false,
    bool IsCarouselRound = false);
