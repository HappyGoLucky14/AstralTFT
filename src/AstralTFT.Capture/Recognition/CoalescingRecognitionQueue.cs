using AstralTFT.Capture.Regions;

namespace AstralTFT.Capture.Recognition;

public sealed record RecognitionWorkItem(
    IRegionObservationDetector Detector,
    IRegionSnapshot Snapshot,
    RecognitionPriority Priority,
    double ChangeScore,
    DateTimeOffset EnqueuedAt)
{
    public string Key => $"{Detector.Descriptor.Id}\u001f{Snapshot.RegionId}";
}

/// <summary>
/// Bounded queue that retains only the newest pending snapshot for each
/// detector/region pair. This prevents stale frames from accumulating when TFT or
/// a recogniser temporarily runs slower than capture.
/// </summary>
public sealed class CoalescingRecognitionQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RecognitionWorkItem> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _available = new(0);
    private readonly int _capacity;
    private bool _disposed;

    public CoalescingRecognitionQueue(int capacity = 32)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate) return _pending.Count;
        }
    }

    public bool Enqueue(RecognitionWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        RecognitionWorkItem? dropped = null;
        RecognitionWorkItem? replaced = null;
        var addedNewKey = false;

        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CoalescingRecognitionQueue));

            if (_pending.TryGetValue(item.Key, out replaced))
            {
                _pending[item.Key] = item;
            }
            else
            {
                if (_pending.Count >= _capacity)
                {
                    var victim = _pending.Values
                        .OrderBy(x => x.Priority)
                        .ThenBy(x => x.Snapshot.FrameSequence)
                        .ThenBy(x => x.EnqueuedAt)
                        .First();

                    // Never evict a more important pending decision for lower-priority work.
                    if (victim.Priority > item.Priority)
                        return false;

                    _pending.Remove(victim.Key);
                    dropped = victim;
                }

                _pending[item.Key] = item;
                addedNewKey = dropped is null;
            }
        }

        if (replaced is not null)
            replaced.Snapshot.Dispose();

        if (dropped is not null)
        {
            dropped.Snapshot.Dispose();
            // The semaphore already contains one token for the removed pending key;
            // replacing that key with another keeps the total token count correct.
        }
        else if (addedNewKey)
        {
            _available.Release();
        }

        return true;
    }

    public async ValueTask<RecognitionWorkItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(CoalescingRecognitionQueue));
                if (_pending.Count == 0)
                    continue;

                var next = _pending.Values
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.EnqueuedAt)
                    .ThenByDescending(x => x.Snapshot.FrameSequence)
                    .First();

                _pending.Remove(next.Key);
                return next;
            }
        }
    }

    public void Dispose()
    {
        RecognitionWorkItem[] remaining;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            remaining = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (var work in remaining)
            work.Snapshot.Dispose();

        _available.Dispose();
    }
}
