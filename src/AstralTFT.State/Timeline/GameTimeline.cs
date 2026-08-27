using AstralTFT.State.Events;
using AstralTFT.State.Reducers;
using AstralTFT.Core.Models;

namespace AstralTFT.State.Timeline;

public sealed record EventEnvelope(long Sequence, GameEvent Event);

public sealed class GameTimeline
{
    private readonly List<EventEnvelope> _events = new();
    private readonly IGameStateReducer _reducer;

    public GameTimeline(GameState initialState, IGameStateReducer? reducer = null)
    {
        State = initialState;
        _reducer = reducer ?? new GameStateReducer();
    }

    public GameState State { get; private set; }
    public IReadOnlyList<EventEnvelope> Events => _events;

    public EventEnvelope Append(GameEvent gameEvent)
    {
        if (gameEvent.GameId != State.GameId)
            throw new InvalidOperationException("Event belongs to a different game.");

        State = _reducer.Apply(State, gameEvent);
        var envelope = new EventEnvelope(_events.Count + 1L, gameEvent);
        _events.Add(envelope);
        return envelope;
    }

    public static GameState Replay(GameState initialState, IEnumerable<EventEnvelope> events, IGameStateReducer? reducer = null)
    {
        var state = initialState;
        var stateReducer = reducer ?? new GameStateReducer();
        long expected = 1;

        foreach (var envelope in events.OrderBy(x => x.Sequence))
        {
            if (envelope.Sequence != expected)
                throw new InvalidOperationException($"Timeline sequence gap. Expected {expected}, got {envelope.Sequence}.");

            state = stateReducer.Apply(state, envelope.Event);
            expected++;
        }

        return state;
    }
}
