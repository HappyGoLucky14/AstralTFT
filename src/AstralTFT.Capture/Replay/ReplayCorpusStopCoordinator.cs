namespace AstralTFT.Capture.Replay;

/// <summary>
/// Result of stopping a capture source while guaranteeing that a detached replay
/// corpus recorder has drained all work it accepted before shutdown completes.
/// </summary>
public sealed record ReplayCorpusStopResult(
    bool CaptureConsumerStopped,
    bool SourceStopTimedOut);

/// <summary>
/// Publishes one stop operation at a time. All overlapping callers receive the
/// same task, and a later stop can start only after the published operation has
/// completed its full caller-supplied cleanup body.
/// </summary>
public sealed class ReplayCorpusStopGate
{
    private readonly object _gate = new();
    private Task? _inFlight;

    /// <summary>
    /// Starts <paramref name="stopAsync"/> only when no stop is in flight.
    /// </summary>
    public Task RunAsync(Func<Task> stopAsync)
    {
        ArgumentNullException.ThrowIfNull(stopAsync);

        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_inFlight is not null)
                return _inFlight;

            // Publish before invoking the core so a synchronously completed core
            // cannot leave a second caller observing an empty gate mid-start.
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = completion.Task;
        }

        _ = RunAndClearAsync(stopAsync, completion);
        return completion.Task;
    }

    private async Task RunAndClearAsync(Func<Task> stopAsync, TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            await stopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // Clear after the complete core body has finished, but before publishing
        // completion, so a caller that observes a completed task can begin a new
        // lifecycle operation without receiving a stale completed task.
        lock (_gate)
        {
            if (ReferenceEquals(_inFlight, completion.Task))
                _inFlight = null;
        }

        if (failure is null)
            completion.TrySetResult();
        else
            completion.TrySetException(failure);
    }
}

/// <summary>
/// Publishes one application-shutdown operation for the lifetime of its owner.
/// Every caller receives the same task, including callers that arrive after the
/// shutdown has completed or faulted.
/// </summary>
public sealed class ReplayCorpusShutdownGate
{
    private readonly object _gate = new();
    private Task? _shutdownTask;

    /// <summary>
    /// Starts <paramref name="shutdownAsync"/> once and retains its completion
    /// or fault for every later caller.
    /// </summary>
    public Task RunAsync(Func<Task> shutdownAsync)
    {
        ArgumentNullException.ThrowIfNull(shutdownAsync);

        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_shutdownTask is not null)
                return _shutdownTask;

            // Publish before invoking the core so synchronous completion or a
            // re-entrant close cannot observe an empty shutdown gate.
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdownTask = completion.Task;
        }

        _ = RunAndCompleteAsync(shutdownAsync, completion);
        return completion.Task;
    }

    private static async Task RunAndCompleteAsync(Func<Task> shutdownAsync, TaskCompletionSource completion)
    {
        try
        {
            await shutdownAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}

/// <summary>
/// Coordinates source shutdown with the independent, detached corpus writer.
/// </summary>
public static class ReplayCorpusStopCoordinator
{
    /// <summary>
    /// Starts the recorder drain before waiting for the capture source. When the
    /// source does stop, its consumer is awaited before the retained drain task.
    /// A source-stop timeout deliberately skips an unbounded consumer wait, but
    /// still awaits the detached recorder because no later producer can submit.
    /// </summary>
    public static async Task<ReplayCorpusStopResult> StopAndDrainAsync(
        BoundedRegionCorpusRecorder? corpusRecorder,
        Task? captureConsumerTask,
        Func<CancellationToken, Task>? stopSourceAsync,
        TimeSpan sourceStopTimeout)
    {
        if (stopSourceAsync is not null && sourceStopTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sourceStopTimeout));

        Task? corpusDrainTask = null;
        if (corpusRecorder is not null)
        {
            try
            {
                corpusDrainTask = corpusRecorder.DisposeAsync().AsTask();
            }
            catch
            {
                // Corpus shutdown is diagnostic-only and must not stop the source.
            }
        }

        var captureConsumerStopped = stopSourceAsync is null;
        var sourceStopTimedOut = false;
        if (stopSourceAsync is not null)
        {
            using var stopWait = new CancellationTokenSource(sourceStopTimeout);
            try
            {
                await stopSourceAsync(stopWait.Token).ConfigureAwait(false);
                captureConsumerStopped = true;
            }
            catch (OperationCanceledException) when (stopWait.IsCancellationRequested)
            {
                sourceStopTimedOut = true;
            }
            catch
            {
                // Source faults are reported through its normal telemetry/events.
            }
        }

        if (captureConsumerStopped && captureConsumerTask is not null)
        {
            try { await captureConsumerTask.ConfigureAwait(false); } catch { }
        }

        if (corpusDrainTask is not null)
        {
            try { await corpusDrainTask.ConfigureAwait(false); } catch { }
        }

        return new ReplayCorpusStopResult(captureConsumerStopped, sourceStopTimedOut);
    }
}
