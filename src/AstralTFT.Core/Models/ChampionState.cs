namespace AstralTFT.Core.Models;

public enum UnitLocation
{
    Board,
    Bench,
    Shop,
    Unknown
}

public enum AcquisitionSource
{
    Unknown,
    ShopPurchase,
    PveOrLoot,
    Carousel,
    Duplicator,
    SpecialMechanic
}

public sealed record UnitInstance(
    Guid InstanceId,
    string ChampionId,
    int StarLevel,
    UnitLocation Location,
    int Slot,
    IReadOnlyList<string> ItemIds,
    Confidence Confidence,
    DateTimeOffset FirstSeenAt,
    AcquisitionSource AcquisitionSource)
{
    public int BaseCopyEquivalent => StarLevel switch
    {
        <= 1 => 1,
        2 => 3,
        _ => 9
    };
}
