using AstralTFT.Core.Models;
using AstralTFT.State.Events;
using AstralTFT.State.Tracking;

namespace AstralTFT.State.Reducers;

public interface IGameStateReducer
{
    GameState Apply(GameState state, GameEvent gameEvent);
}

public sealed class GameStateReducer : IGameStateReducer
{
    public GameState Apply(GameState state, GameEvent gameEvent)
    {
        if (state.GameId != gameEvent.GameId)
            throw new InvalidOperationException("Cannot apply an event from a different game.");

        var updated = gameEvent switch
        {
            StageChangedEvent e => ApplyStage(state, e),
            RoundPhaseChangedEvent e => ApplyRoundPhase(state, e),
            GoldChangedEvent e => state with { Player = state.Player with { Gold = e.CurrentGold } },
            HpChangedEvent e => state with { Player = state.Player with { Hp = e.CurrentHp } },
            LevelChangedEvent e => state with { Player = state.Player with { Level = e.CurrentLevel } },
            XpChangedEvent e => state with { Player = state.Player with { Xp = e.CurrentXp } },
            RosterSnapshotAcceptedEvent e => ApplyRoster(state, e),
            ShopSnapshotAcceptedEvent e => state with { Shop = e.Shop },
            ItemBenchSnapshotAcceptedEvent e => state with { ItemBench = e.ItemIds },
            AugmentsSnapshotAcceptedEvent e => state with { Augments = e.AugmentIds },
            TraitsSnapshotAcceptedEvent e => state with { TraitCounts = e.TraitCounts },
            CosmeticsObservedEvent e => state with { Cosmetics = e.Cosmetics },
            AugmentOfferObservedEvent e => state with { ActiveAugmentOffer = e.Offer },
            AugmentSelectedEvent e => ApplyAugmentSelected(state, e),
            OpeningEncounterObservedEvent e => state with { OpeningEncounterId = e.EncounterId },
            _ => state
        };

        return updated with { UpdatedAt = gameEvent.At };
    }

    private static GameState ApplyStage(GameState state, StageChangedEvent gameEvent)
    {
        StagePoint? point = null;
        if (StagePoint.TryParse(gameEvent.CurrentStage, out var parsed)) point = parsed;

        return state with
        {
            Stage = gameEvent.CurrentStage,
            Round = state.Round with { Stage = point }
        };
    }

    private static GameState ApplyRoundPhase(GameState state, RoundPhaseChangedEvent gameEvent) => state with
    {
        Round = state.Round with
        {
            Phase = gameEvent.CurrentPhase,
            PhaseStartedAt = gameEvent.At,
            IsPveRound = gameEvent.IsPveRound,
            IsAugmentRound = gameEvent.IsAugmentRound,
            IsCarouselRound = gameEvent.IsCarouselRound
        }
    };

    private static GameState ApplyAugmentSelected(GameState state, AugmentSelectedEvent gameEvent)
    {
        var augments = state.Augments
            .Concat(new[] { gameEvent.AugmentId })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return state with
        {
            Augments = augments,
            ActiveAugmentOffer = null
        };
    }

    private static GameState ApplyRoster(GameState state, RosterSnapshotAcceptedEvent gameEvent)
    {
        var copies = OwnershipTracker.CountBaseCopies(gameEvent.Board, gameEvent.Bench);
        return state with
        {
            Board = gameEvent.Board,
            Bench = gameEvent.Bench,
            OwnedBaseCopies = copies
        };
    }
}
