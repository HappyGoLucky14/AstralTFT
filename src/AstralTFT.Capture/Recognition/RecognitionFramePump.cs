using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Regions;

namespace AstralTFT.Capture.Recognition;

public sealed record RecognitionPumpBudget(
    int MaxChangedRegionsPerFrame,
    int MaxSubmissionsPerFrame)
{
    public static RecognitionPumpBudget Sleeping { get; } = new(0, 0);
    public static RecognitionPumpBudget Balanced { get; } = new(4, 4);
}

public interface IRecognitionPumpBudgetProvider
{
    RecognitionPumpBudget Current { get; }
}

public sealed record FrameRoutingTelemetry(
    long FrameSequence,
    DateTimeOffset CapturedAt,
    int ChangedRegions,
    RecognitionRoutingResult Routing,
    TimeSpan ProcessingTime);

/// <summary>
/// Owns the hot path from one capture-frame lease to changed-region recognition
/// work. It always disposes each capture frame after stable ROI snapshots have been
/// created, keeping native/GPU lifetime short and bounded.
/// </summary>
public sealed class RecognitionFramePump
{
    private readonly IFrameSource _frameSource;
    private readonly ChangedRegionSelector _selector;
    private readonly RecognitionFrameRouter _router;
    private readonly Func<CapturedFrame, IReadOnlyList<RecognitionRegion>> _regions;
    private readonly IRecognitionPumpBudgetProvider _budget;

    public RecognitionFramePump(
        IFrameSource frameSource,
        ChangedRegionSelector selector,
        RecognitionFrameRouter router,
        Func<CapturedFrame, IReadOnlyList<RecognitionRegion>> regions,
        IRecognitionPumpBudgetProvider budget)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _regions = regions ?? throw new ArgumentNullException(nameof(regions));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
    }

    public event EventHandler<FrameRoutingTelemetry>? FrameProcessed;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _frameSource.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var frame in _frameSource.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                using (frame)
                {
                    var started = DateTimeOffset.UtcNow;
                    var budget = _budget.Current;
                    if (budget.MaxChangedRegionsPerFrame <= 0 || budget.MaxSubmissionsPerFrame <= 0)
                    {
                        Emit(new FrameRoutingTelemetry(
                            frame.Sequence,
                            frame.CapturedAt,
                            0,
                            new RecognitionRoutingResult(0, 0, 0, 0, 0),
                            DateTimeOffset.UtcNow - started));
                        continue;
                    }

                    var definitions = _regions(frame);
                    var changed = _selector.Select(
                        frame,
                        definitions,
                        budget.MaxChangedRegionsPerFrame);

                    var routing = _router.Route(
                        frame,
                        changed,
                        budget.MaxSubmissionsPerFrame,
                        DateTimeOffset.UtcNow);

                    Emit(new FrameRoutingTelemetry(
                        frame.Sequence,
                        frame.CapturedAt,
                        changed.Count,
                        routing,
                        DateTimeOffset.UtcNow - started));
                }
            }
        }
        finally
        {
            await _frameSource.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void Emit(FrameRoutingTelemetry telemetry)
    {
        try
        {
            FrameProcessed?.Invoke(this, telemetry);
        }
        catch
        {
            // Diagnostics/UI listeners must not terminate capture.
        }
    }
}

public sealed class FixedRecognitionPumpBudgetProvider : IRecognitionPumpBudgetProvider
{
    public FixedRecognitionPumpBudgetProvider(RecognitionPumpBudget initial) => Current = initial;
    public RecognitionPumpBudget Current { get; set; }
}
