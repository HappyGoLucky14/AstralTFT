namespace AstralTFT.Infrastructure.Diagnostics;

public enum PerformanceMode
{
    Automatic = 0,
    Eco = 1,
    Balanced = 2,
    Responsive = 3
}

public sealed record PerformanceSample(
    DateTimeOffset At,
    double ProcessCpuPercent,
    long WorkingSetBytes,
    double? ProcessGpuPercent,
    int RecognitionQueueDepth,
    TimeSpan P95RecognitionLatency,
    bool TftForeground = true,
    bool TftMinimized = false);

public sealed record WorkBudget(
    int MaxConcurrentDetectors,
    TimeSpan MinRegionRecheckInterval,
    bool AllowGpuInference,
    bool AggressiveIdleBackoff,
    int MaxChangedRegionsPerFrame = 4);

public interface IPerformanceGovernor
{
    PerformanceMode Mode { get; }
    WorkBudget CurrentBudget { get; }
    void SetMode(PerformanceMode mode);
    void Observe(PerformanceSample sample);
}
