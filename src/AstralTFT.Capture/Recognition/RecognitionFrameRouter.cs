using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Regions;

namespace AstralTFT.Capture.Recognition;

public sealed record RecognitionRoutingResult(
    int Requested,
    int Submitted,
    int Unsupported,
    int SnapshotFailures,
    int QueueRejected);

/// <summary>
/// Bridges cheap frame/ROI change detection to asynchronous recognition. It copies
/// only changed, supported regions into stable snapshots before the capture frame
/// can be released.
/// </summary>
public sealed class RecognitionFrameRouter
{
    private readonly RecognitionDetectorRegistry _registry;
    private readonly IRegionSnapshotFactory _snapshotFactory;
    private readonly RecognitionDispatcher _dispatcher;

    public RecognitionFrameRouter(
        RecognitionDetectorRegistry registry,
        IRegionSnapshotFactory snapshotFactory,
        RecognitionDispatcher dispatcher)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public RecognitionRoutingResult Route(
        CapturedFrame frame,
        IEnumerable<ChangedRegionWork> changedRegions,
        int maxSubmissions = int.MaxValue,
        DateTimeOffset? now = null)
    {
        if (maxSubmissions <= 0)
            return new RecognitionRoutingResult(0, 0, 0, 0, 0);

        var requested = 0;
        var submitted = 0;
        var unsupported = 0;
        var snapshotFailures = 0;
        var queueRejected = 0;
        var observedNow = now ?? DateTimeOffset.UtcNow;

        foreach (var work in changedRegions
                     .OrderByDescending(x => x.Priority)
                     .ThenByDescending(x => x.ChangeScore))
        {
            if (submitted >= maxSubmissions) break;
            requested++;

            if (!_registry.TryResolve(work.Region.Id, out var detector))
            {
                unsupported++;
                continue;
            }

            IRegionSnapshot snapshot;
            try
            {
                snapshot = _snapshotFactory.Create(frame, work.Region);
            }
            catch
            {
                snapshotFailures++;
                continue;
            }

            if (_dispatcher.Submit(detector, snapshot, work.Priority, work.ChangeScore, observedNow))
                submitted++;
            else
                queueRejected++;
        }

        return new RecognitionRoutingResult(
            requested,
            submitted,
            unsupported,
            snapshotFailures,
            queueRejected);
    }
}
