namespace AstralTFT.Capture.Replay;

/// <summary>
/// Result of stopping a capture source while guaranteeing that a detached replay
/// corpus recorder has drained all work it accepted before shutdown completes.
/// </summary>
public sealed record ReplayCorpusStopResult(
    bool CaptureConsumerStopped,
    bool SourceStopTimedOut);

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
