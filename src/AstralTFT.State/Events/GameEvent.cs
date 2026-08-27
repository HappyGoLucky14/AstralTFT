using AstralTFT.Core.Models;

namespace AstralTFT.State.Events;

public abstract record GameEvent(Guid GameId, DateTimeOffset At);

public sealed record UnitAcquiredEvent(
    Guid GameId,
    DateTimeOffset At,
    string ChampionId,
    AcquisitionSource Source,
    int BaseCopies,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record UnitPurchasedEvent(
    Guid GameId,
    DateTimeOffset At,
    string ChampionId,
    int GoldBefore,
    int GoldAfter,
    Confidence Confidence,
    int BaseCopies = 1) : GameEvent(GameId, At);

public sealed record UnitSoldEvent(
    Guid GameId,
    DateTimeOffset At,
    string ChampionId,
    int BaseCopies,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record StageChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    string? PreviousStage,
    string CurrentStage) : GameEvent(GameId, At);

public sealed record RoundPhaseChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    RoundPhase PreviousPhase,
    RoundPhase CurrentPhase,
    bool IsPveRound = false,
    bool IsAugmentRound = false,
    bool IsCarouselRound = false) : GameEvent(GameId, At);

public sealed record GoldChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    int? PreviousGold,
    int CurrentGold) : GameEvent(GameId, At);

public sealed record HpChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    int? PreviousHp,
    int CurrentHp) : GameEvent(GameId, At);

public sealed record LevelChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    int? PreviousLevel,
    int CurrentLevel) : GameEvent(GameId, At);

public sealed record XpChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    int? PreviousXp,
    int CurrentXp) : GameEvent(GameId, At);

public sealed record RosterSnapshotAcceptedEvent(
    Guid GameId,
    DateTimeOffset At,
    IReadOnlyList<UnitInstance> Board,
    IReadOnlyList<UnitInstance> Bench,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record ShopSnapshotAcceptedEvent(
    Guid GameId,
    DateTimeOffset At,
    IReadOnlyList<Observation<ShopEntry?>> Shop,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record ItemBenchSnapshotAcceptedEvent(
    Guid GameId,
    DateTimeOffset At,
    IReadOnlyList<string> ItemIds,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record AugmentsSnapshotAcceptedEvent(
    Guid GameId,
    DateTimeOffset At,
    IReadOnlyList<string> AugmentIds,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record TraitsSnapshotAcceptedEvent(
    Guid GameId,
    DateTimeOffset At,
    IReadOnlyDictionary<string, int> TraitCounts,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record CosmeticsObservedEvent(
    Guid GameId,
    DateTimeOffset At,
    CosmeticState Cosmetics,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record WispPurchasedEvent(
    Guid GameId,
    DateTimeOffset At,
    string? WispId,
    WispCategory Category,
    int GoldBefore,
    int GoldAfter,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record ShopRefreshedEvent(
    Guid GameId,
    DateTimeOffset At,
    int GoldBefore,
    int GoldAfter,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record ExperiencePurchasedEvent(
    Guid GameId,
    DateTimeOffset At,
    int GoldBefore,
    int GoldAfter,
    int? XpBefore,
    int? XpAfter,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record UnitMovedEvent(
    Guid GameId,
    DateTimeOffset At,
    Guid UnitInstanceId,
    string ChampionId,
    UnitLocation From,
    UnitLocation To,
    int FromSlot,
    int ToSlot,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record StarLevelChangedEvent(
    Guid GameId,
    DateTimeOffset At,
    string ChampionId,
    int PreviousStarLevel,
    int CurrentStarLevel,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record ItemEquippedEvent(
    Guid GameId,
    DateTimeOffset At,
    string ItemId,
    Guid UnitInstanceId,
    string ChampionId,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record ItemMovedEvent(
    Guid GameId,
    DateTimeOffset At,
    string ItemId,
    Guid? FromUnitInstanceId,
    Guid? ToUnitInstanceId,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record AugmentOfferObservedEvent(
    Guid GameId,
    DateTimeOffset At,
    AugmentOfferState Offer) : GameEvent(GameId, At);

public sealed record AugmentSelectedEvent(
    Guid GameId,
    DateTimeOffset At,
    string AugmentId,
    int OfferIndex,
    Confidence Confidence) : GameEvent(GameId, At);

public sealed record OpeningEncounterObservedEvent(
    Guid GameId,
    DateTimeOffset At,
    string EncounterId,
    Confidence Confidence) : GameEvent(GameId, At);
