using System.Text;
using System.Threading.Channels;
using AstralTFT.Capture.Recognition;

namespace AstralTFT.Capture.Replay;

public sealed record RegionCorpusRecorderMetrics(
    long Accepted,
    long Dropped,
    long Written,
    long Deduplicated,
    long Failed,
    long Pending,
    string? LastDiagnostic);

/// <summary>
/// Copies completed CPU snapshots to a bounded background writer so capture work
/// never waits on corpus I/O.
/// </summary>
public sealed class BoundedRegionCorpusRecorder : IAsyncDisposable
{
    private const int DefaultCapacity = 16;
    private const int MaximumDiagnosticLength = 512;

    private readonly Channel<RegionCorpusWriteRequest> _requests;
    private readonly IRegionCorpusSink _sink;
    private readonly Task _worker;
    private readonly object _disposeGate = new();

    private Task? _disposeTask;
    private string? _lastDiagnostic;
    private long _accepted;
    private long _dropped;
    private long _written;
    private long _deduplicated;
    private long _failed;
    private long _pending;
    private int _accepting = 1;

    public BoundedRegionCorpusRecorder(IRegionCorpusSink sink, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _sink = sink;
        _requests = Channel.CreateBounded<RegionCorpusWriteRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _worker = Task.Run(ProcessAsync);
    }

    public RegionCorpusRecorderMetrics Metrics => new(
        Interlocked.Read(ref _accepted),
        Interlocked.Read(ref _dropped),
        Interlocked.Read(ref _written),
        Interlocked.Read(ref _deduplicated),
        Interlocked.Read(ref _failed),
        Interlocked.Read(ref _pending),
        Volatile.Read(ref _lastDiagnostic));

    /// <summary>
    /// Records a snapshot when queue capacity is immediately available. This
    /// method never waits for a writer or for corpus storage.
    /// </summary>
    public bool TryRecord(Bgra32RegionSnapshot snapshot, RegionCorpusSourceKind sourceKind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));

        if (Volatile.Read(ref _accepting) == 0)
            return Drop();

        var request = new RegionCorpusWriteRequest(
            snapshot.RegionId,
            snapshot.FrameSequence,
            snapshot.CapturedAt,
            snapshot.Width,
            snapshot.Height,
            snapshot.Stride,
            snapshot.Pixels.ToArray(),
            sourceKind);

        if (Volatile.Read(ref _accepting) == 0)
            return Drop();

        Interlocked.Increment(ref _pending);
        Interlocked.Increment(ref _accepted);
        if (_requests.Writer.TryWrite(request))
            return true;

        Interlocked.Decrement(ref _accepted);
        Interlocked.Decrement(ref _pending);
        return Drop();
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is null)
            {
                Interlocked.Exchange(ref _accepting, 0);
                _requests.Writer.TryComplete();
                _disposeTask = _worker;
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task ProcessAsync()
    {
        await foreach (var request in _requests.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var result = await _sink.WriteAsync(request).ConfigureAwait(false);
                Interlocked.Increment(ref _written);
                if (!result.BlobCreated)
                    Interlocked.Increment(ref _deduplicated);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _failed);
                Volatile.Write(ref _lastDiagnostic, SanitizeDiagnostic(exception));
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    private bool Drop()
    {
        Interlocked.Increment(ref _dropped);
        return false;
    }

    private static string SanitizeDiagnostic(Exception exception)
    {
        var raw = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : $"{exception.GetType().Name}: {exception.Message}";
        var diagnostic = new StringBuilder(Math.Min(raw.Length, MaximumDiagnosticLength));
        var previousSpace = false;

        foreach (var character in raw)
        {
            var sanitized = char.IsControl(character) || char.IsWhiteSpace(character) ? ' ' : character;
            if (sanitized == ' ' && previousSpace)
                continue;

            if (diagnostic.Length == MaximumDiagnosticLength)
                break;

            diagnostic.Append(sanitized);
            previousSpace = sanitized == ' ';
        }

        return diagnostic.ToString().Trim();
    }
}
