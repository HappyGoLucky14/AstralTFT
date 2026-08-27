using AstralTFT.Capture.Regions;

namespace AstralTFT.Capture.Recognition;

public sealed record RecognitionDispatchMetrics(
    long Submitted,
    long CoalescedOrAccepted,
    long Rejected,
    long Completed,
    long Failed,
    long SkippedStale,
    long SkippedUnhealthy);

/// <summary>
/// Executes region detectors on a small bounded worker pool. Pending work is
/// coalesced per detector/region so recognition latency stays bounded under load.
/// </summary>
public sealed class RecognitionDispatcher : IAsyncDisposable
{
    private readonly CoalescingRecognitionQueue _queue;
    private readonly DetectorHealthTracker _health;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _workers = new();
    private readonly int _workerCount;
    private int _started;

    private long _submitted;
    private long _accepted;
    private long _rejected;
    private long _completed;
    private long _failed;
    private long _stale;
    private long _unhealthy;

    public RecognitionDispatcher(
        int workerCount = 2,
        int queueCapacity = 32,
        DetectorHealthTracker? healthTracker = null)
    {
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        _workerCount = workerCount;
        _queue = new CoalescingRecognitionQueue(queueCapacity);
        _health = healthTracker ?? new DetectorHealthTracker();
    }

    public event EventHandler<RecognitionBatch>? BatchCompleted;

    public RecognitionDispatchMetrics Metrics => new(
        Interlocked.Read(ref _submitted),
        Interlocked.Read(ref _accepted),
        Interlocked.Read(ref _rejected),
        Interlocked.Read(ref _completed),
        Interlocked.Read(ref _failed),
        Interlocked.Read(ref _stale),
        Interlocked.Read(ref _unhealthy));

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        for (var i = 0; i < _workerCount; i++)
            _workers.Add(Task.Run(() => WorkerLoopAsync(_shutdown.Token)));
    }

    public bool Submit(
        IRegionObservationDetector detector,
        IRegionSnapshot snapshot,
        RecognitionPriority priority,
        double changeScore,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Increment(ref _submitted);

        var item = new RecognitionWorkItem(detector, snapshot, priority, changeScore, now);
        bool accepted;
        try
        {
            accepted = _queue.Enqueue(item);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }

        if (accepted)
            Interlocked.Increment(ref _accepted);
        else
        {
            Interlocked.Increment(ref _rejected);
            snapshot.Dispose();
        }

        return accepted;
    }

    private async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var work = await _queue.DequeueAsync(cancellationToken).ConfigureAwait(false);
                using (work.Snapshot)
                {
                    var descriptor = work.Detector.Descriptor;
                    var now = DateTimeOffset.UtcNow;

                    if (!_health.CanRun(descriptor, now))
                    {
                        Interlocked.Increment(ref _unhealthy);
                        Emit(RecognitionBatch.Empty(
                            descriptor.Id,
                            work.Snapshot,
                            now,
                            RecognitionBatchStatus.SkippedUnhealthy,
                            "Detector circuit breaker is cooling down or disabled."));
                        continue;
                    }

                    if (descriptor.StaleAfter > TimeSpan.Zero && now - work.Snapshot.CapturedAt > descriptor.StaleAfter)
                    {
                        Interlocked.Increment(ref _stale);
                        Emit(RecognitionBatch.Empty(
                            descriptor.Id,
                            work.Snapshot,
                            now,
                            RecognitionBatchStatus.SkippedStale,
                            "Recognition work became stale before execution."));
                        continue;
                    }

                    try
                    {
                        var batch = await work.Detector
                            .DetectAsync(work.Snapshot, cancellationToken)
                            .ConfigureAwait(false);

                        var completedAt = DateTimeOffset.UtcNow;
                        if (batch.Status == RecognitionBatchStatus.Failed)
                        {
                            _health.RecordFailure(descriptor, completedAt, batch.Diagnostic);
                            Interlocked.Increment(ref _failed);
                        }
                        else
                        {
                            _health.RecordSuccess(descriptor.Id, completedAt);
                        }

                        Emit(batch);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        var completedAt = DateTimeOffset.UtcNow;
                        _health.RecordFailure(descriptor, completedAt, ex.Message);
                        Interlocked.Increment(ref _failed);
                        Emit(RecognitionBatch.Empty(
                            descriptor.Id,
                            work.Snapshot,
                            completedAt,
                            RecognitionBatchStatus.Failed,
                            ex.GetType().Name + ": " + ex.Message));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Emit(RecognitionBatch batch)
    {
        Interlocked.Increment(ref _completed);
        try
        {
            BatchCompleted?.Invoke(this, batch);
        }
        catch
        {
            // A UI/telemetry subscriber must never terminate recognition workers.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _queue.Dispose();
            _shutdown.Dispose();
        }
    }
}
