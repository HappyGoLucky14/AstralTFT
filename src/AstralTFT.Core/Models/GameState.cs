namespace AstralTFT.Core.Models;

public sealed record PlayerState(
    int? Hp,
    int? Gold,
    int? Level,
    int? Xp,
    string? Rank,
    int? LeaguePoints);

public sealed record CosmeticState(
    string? ArenaId,
    string? TacticianId,
    string? BoomId = null,
    string? PortalId = null);

public sealed record AugmentOfferState(
    int OfferIndex,
    IReadOnlyList<string> OfferedAugmentIds,
    int RerollsRemaining,
    DateTimeOffset ObservedAt,
    Confidence Confidence);

public sealed record GameState(
    Guid GameId,
    string? Patch,
    string? SetId,
    string? QueueType,
    DateTimeOffset UpdatedAt,
    string? Stage,
    RoundState Round,
    PlayerState Player,
    IReadOnlyList<UnitInstance> Board,
    IReadOnlyList<UnitInstance> Bench,
    IReadOnlyList<Observation<ShopEntry?>> Shop,
    IReadOnlyList<string> ItemBench,
    IReadOnlyList<string> Augments,
    IReadOnlyDictionary<string, int> TraitCounts,
    IReadOnlyDictionary<string, int> OwnedBaseCopies,
    CosmeticState Cosmetics,
    AugmentOfferState? ActiveAugmentOffer = null,
    string? OpeningEncounterId = null)
{
    public static GameState Empty(DateTimeOffset now) => new(
        Guid.NewGuid(), null, null, null, now, null,
        new RoundState(null, RoundPhase.Unknown),
        new PlayerState(null, null, null, null, null, null),
        Array.Empty<UnitInstance>(),
        Array.Empty<UnitInstance>(),
        Array.Empty<Observation<ShopEntry?>>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        new CosmeticState(null, null));
}
