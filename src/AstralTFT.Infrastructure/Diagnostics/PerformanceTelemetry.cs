namespace AstralTFT.Infrastructure.Diagnostics;

public sealed record DetectorTimingSample(
    string DetectorId,
    DateTimeOffset At,
    TimeSpan Duration,
    bool Accepted,
    bool DroppedAsStale = false);

public sealed record DetectorTimingSummary(
    string DetectorId,
    int Count,
    TimeSpan P50,
    TimeSpan P95,
    int AcceptedCount,
    int StaleDropCount);

public sealed record PerformanceTelemetrySnapshot(
    DateTimeOffset GeneratedAt,
    int CaptureFramesObserved,
    int ChangedRegionsObserved,
    IReadOnlyList<DetectorTimingSummary> Detectors,
    PerformanceSample? LatestProcessSample);

/// <summary>
/// Small in-memory diagnostics buffer. It is bounded so leaving diagnostics enabled does not turn
/// into an unbounded allocation problem during long sessions.
/// </summary>
public sealed class PerformanceTelemetry
{
    private readonly object _gate = new();
    private readonly Queue<DetectorTimingSample> _detectorSamples = new();
    private readonly int _maxDetectorSamples;
    private int _captureFrames;
    private int _changedRegions;
    private PerformanceSample? _latestProcessSample;

    public PerformanceTelemetry(int maxDetectorSamples = 2_000)
    {
        if (maxDetectorSamples < 100) throw new ArgumentOutOfRangeException(nameof(maxDetectorSamples));
        _maxDetectorSamples = maxDetectorSamples;
    }

    public void RecordCaptureFrame()
    {
        lock (_gate) _captureFrames++;
    }

    public void RecordChangedRegions(int count)
    {
        if (count <= 0) return;
        lock (_gate) _changedRegions += count;
    }

    public void RecordDetector(DetectorTimingSample sample)
    {
        lock (_gate)
        {
            _detectorSamples.Enqueue(sample);
            while (_detectorSamples.Count > _maxDetectorSamples)
                _detectorSamples.Dequeue();
        }
    }

    public void RecordProcessSample(PerformanceSample sample)
    {
        lock (_gate) _latestProcessSample = sample;
    }

    public PerformanceTelemetrySnapshot Snapshot(DateTimeOffset now)
    {
        lock (_gate)
        {
            var summaries = _detectorSamples
                .GroupBy(x => x.DetectorId, StringComparer.OrdinalIgnoreCase)
                .Select(g => Summarize(g.Key, g.ToArray()))
                .OrderByDescending(x => x.P95)
                .ToArray();

            return new PerformanceTelemetrySnapshot(
                now,
                _captureFrames,
                _changedRegions,
                summaries,
                _latestProcessSample);
        }
    }

    private static DetectorTimingSummary Summarize(string id, IReadOnlyList<DetectorTimingSample> samples)
    {
        var orderedTicks = samples.Select(x => x.Duration.Ticks).OrderBy(x => x).ToArray();
        return new DetectorTimingSummary(
            id,
            samples.Count,
            TimeSpan.FromTicks(Percentile(orderedTicks, .50)),
            TimeSpan.FromTicks(Percentile(orderedTicks, .95)),
            samples.Count(x => x.Accepted),
            samples.Count(x => x.DroppedAsStale));
    }

    private static long Percentile(IReadOnlyList<long> ordered, double percentile)
    {
        if (ordered.Count == 0) return 0;
        if (ordered.Count == 1) return ordered[0];
        var index = (int)Math.Ceiling(Math.Clamp(percentile, 0, 1) * ordered.Count) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }
}
