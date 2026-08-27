namespace AstralTFT.Core.Models;

public enum ShopEntryKind
{
    Unknown = 0,
    Champion = 1,
    Wisp = 2
}

public enum WispCategory
{
    Unknown = 0,
    Champion,
    Combat,
    Misc,
    Shop,
    GoldXp,
    Risky,
    Item
}

public sealed record ShopEntry(
    ShopEntryKind Kind,
    int Slot,
    string? ChampionId = null,
    string? WispId = null,
    WispCategory WispCategory = WispCategory.Unknown,
    string? UnderlyingChampionId = null);
