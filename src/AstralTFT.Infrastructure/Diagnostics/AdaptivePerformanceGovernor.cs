namespace AstralTFT.Infrastructure.Diagnostics;

/// <summary>
/// Keeps recognition responsive while placing a hard preference on not competing with TFT.
/// Automatic mode uses hysteresis so short spikes do not cause visible mode thrashing.
/// </summary>
public sealed class AdaptivePerformanceGovernor : IPerformanceGovernor
{
    private readonly object _gate = new();
    private PerformanceMode _mode = PerformanceMode.Automatic;
    private WorkBudget _current = Balanced();
    private int _highPressureSamples;
    private int _lowPressureSamples;
    private int _sustainedHeadroomSamples;

    public PerformanceMode Mode
    {
        get { lock (_gate) return _mode; }
    }

    public WorkBudget CurrentBudget
    {
        get { lock (_gate) return _current; }
    }

    public void SetMode(PerformanceMode mode)
    {
        lock (_gate)
        {
            _mode = mode;
            _highPressureSamples = 0;
            _lowPressureSamples = 0;
            _sustainedHeadroomSamples = 0;
            _current = mode switch
            {
                PerformanceMode.Eco => Eco(),
                PerformanceMode.Responsive => Responsive(),
                _ => Balanced()
            };
        }
    }

    public void Observe(PerformanceSample sample)
    {
        lock (_gate)
        {
            if (_mode != PerformanceMode.Automatic)
            {
                // Even manual modes back off while TFT is minimized because recognition has no
                // useful reason to burn resources against an unavailable/minimized surface.
                _current = sample.TftMinimized ? Sleeping() : _mode switch
                {
                    PerformanceMode.Eco => Eco(),
                    PerformanceMode.Responsive => Responsive(),
                    _ => Balanced()
                };
                return;
            }

            if (sample.TftMinimized)
            {
                _current = Sleeping();
                ResetCounters();
                return;
            }

            var highPressure =
                sample.ProcessCpuPercent >= 5.0 ||
                sample.ProcessGpuPercent >= 5.0 ||
                sample.RecognitionQueueDepth > 2 ||
                sample.P95RecognitionLatency > TimeSpan.FromMilliseconds(140);

            var lowPressure =
                sample.ProcessCpuPercent < 2.0 &&
                (sample.ProcessGpuPercent is null || sample.ProcessGpuPercent < 2.0) &&
                sample.RecognitionQueueDepth == 0 &&
                sample.P95RecognitionLatency < TimeSpan.FromMilliseconds(65);

            var substantialHeadroom =
                sample.TftForeground &&
                sample.ProcessCpuPercent < 1.25 &&
                (sample.ProcessGpuPercent is null || sample.ProcessGpuPercent < 1.5) &&
                sample.RecognitionQueueDepth == 0 &&
                sample.P95RecognitionLatency < TimeSpan.FromMilliseconds(45);

            if (highPressure)
            {
                _highPressureSamples++;
                _lowPressureSamples = 0;
                _sustainedHeadroomSamples = 0;
            }
            else
            {
                _highPressureSamples = Math.Max(0, _highPressureSamples - 1);
                _lowPressureSamples = lowPressure ? _lowPressureSamples + 1 : Math.Max(0, _lowPressureSamples - 1);
                _sustainedHeadroomSamples = substantialHeadroom
                    ? _sustainedHeadroomSamples + 1
                    : Math.Max(0, _sustainedHeadroomSamples - 1);
            }

            // Fast safety response, slow promotion. Performance is allowed to get better only after
            // sustained headroom; it backs off quickly when our own work begins to queue or spike.
            if (_highPressureSamples >= 3)
            {
                _current = Eco();
                _highPressureSamples = 0;
                return;
            }

            if (_current == Eco() && _lowPressureSamples >= 8)
            {
                _current = Balanced();
                _lowPressureSamples = 0;
                return;
            }

            if (_current == Balanced() && _sustainedHeadroomSamples >= 20)
            {
                _current = Responsive();
                _sustainedHeadroomSamples = 0;
                return;
            }

            // Responsive is opportunistic. Any moderate pressure drops it back to Balanced before
            // waiting for the stricter Eco threshold.
            if (_current == Responsive() && !lowPressure)
            {
                _current = Balanced();
                _sustainedHeadroomSamples = 0;
            }
        }
    }

    private void ResetCounters()
    {
        _highPressureSamples = 0;
        _lowPressureSamples = 0;
        _sustainedHeadroomSamples = 0;
    }

    private static WorkBudget Responsive() => new(
        MaxConcurrentDetectors: 3,
        MinRegionRecheckInterval: TimeSpan.FromMilliseconds(45),
        AllowGpuInference: true,
        AggressiveIdleBackoff: true,
        MaxChangedRegionsPerFrame: 6);

    private static WorkBudget Balanced() => new(
        MaxConcurrentDetectors: 2,
        MinRegionRecheckInterval: TimeSpan.FromMilliseconds(80),
        AllowGpuInference: true,
        AggressiveIdleBackoff: true,
        MaxChangedRegionsPerFrame: 4);

    private static WorkBudget Eco() => new(
        MaxConcurrentDetectors: 1,
        MinRegionRecheckInterval: TimeSpan.FromMilliseconds(180),
        AllowGpuInference: true,
        AggressiveIdleBackoff: true,
        MaxChangedRegionsPerFrame: 2);

    private static WorkBudget Sleeping() => new(
        MaxConcurrentDetectors: 0,
        MinRegionRecheckInterval: TimeSpan.FromSeconds(1),
        AllowGpuInference: false,
        AggressiveIdleBackoff: true,
        MaxChangedRegionsPerFrame: 0);
}
