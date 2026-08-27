using System.Threading.Channels;
using AstralTFT.Core.Models;
using AstralTFT.State.Events;
using AstralTFT.State.Timeline;

namespace AstralTFT.State.Actors;

public sealed record GameStateChanged(
    GameState Previous,
    GameState Current,
    EventEnvelope Envelope);

/// <summary>
/// Single logical writer for accepted game events. Detector/fusion workers publish immutable
/// events; the actor serializes reduction so GameState never has concurrent writers.
/// </summary>
public sealed class GameStateActor : IAsyncDisposable
{
    private readonly Channel<GameEvent> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly GameTimeline _timeline;
    private readonly Task _worker;
    private readonly object _timelineGate = new();
    private volatile GameState _state;

    public GameStateActor(GameState initialState, int capacity = 512)
    {
        if (capacity < 16) throw new ArgumentOutOfRangeException(nameof(capacity));

        _timeline = new GameTimeline(initialState);
        _state = initialState;
        _channel = Channel.CreateBounded<GameEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(() => RunAsync(_shutdown.Token));
    }

    public GameState State => _state;

    public IReadOnlyList<EventEnvelope> GetEventsSnapshot()
    {
        lock (_timelineGate) return _timeline.Events.ToArray();
    }

    public event EventHandler<GameStateChanged>? StateChanged;

    public ValueTask PublishAsync(GameEvent gameEvent, CancellationToken cancellationToken = default)
    {
        if (gameEvent.GameId != _state.GameId)
            throw new InvalidOperationException("Cannot publish an event from a different game.");

        return _channel.Writer.WriteAsync(gameEvent, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _shutdown.CancelAfter(TimeSpan.FromSeconds(2));
        try { await _worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var gameEvent in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            GameState before;
            GameState current;
            EventEnvelope envelope;

            lock (_timelineGate)
            {
                before = _timeline.State;
                envelope = _timeline.Append(gameEvent);
                current = _timeline.State;
                _state = current;
            }

            StateChanged?.Invoke(this, new GameStateChanged(before, current, envelope));
        }
    }
}
