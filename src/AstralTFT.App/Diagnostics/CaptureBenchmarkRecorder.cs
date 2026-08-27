using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Windows;

namespace AstralTFT.App.Diagnostics;

internal sealed record RegionChangeBenchmarkConfiguration(
    string RegionId,
    int GridColumns,
    int GridRows,
    double MeaningfulThreshold);

/// <summary>
/// Records objective capture-overhead data so the first Windows benchmark can be
/// diagnosed without screenshots or manually transcribing counters. It records
/// AstralTFT's own process metrics only; it does not inspect TFT process memory.
/// </summary>
internal sealed class CaptureBenchmarkRecorder
{
    private readonly object _gate = new();
    private readonly GameWindow _window;
    private readonly WgcCaptureOptions _options;
    private readonly RegionChangeBenchmarkConfiguration? _changeConfiguration;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly List<double> _readbackMs = [];
    private readonly List<double> _regionChangeScores = [];
    private readonly List<double> _regionChangeProcessingUs = [];
    private readonly List<ProcessMetricSample> _processSamples = [];
    private long _meaningfulRegionChanges;
    private WgcCaptureTelemetry? _lastTelemetry;
    private long _lastReadbackCount;
    private TimeSpan _previousCpu;
    private DateTimeOffset _previousProcessSampleAt;
    private Task<string?>? _completionTask;

    public CaptureBenchmarkRecorder(
        GameWindow window,
        WgcCaptureOptions options,
        RegionChangeBenchmarkConfiguration? changeConfiguration = null)
    {
        _window = window;
        _options = options;
        _changeConfiguration = changeConfiguration;
        using var process = Process.GetCurrentProcess();
        _previousCpu = process.TotalProcessorTime;
        _previousProcessSampleAt = DateTimeOffset.UtcNow;
    }

    public string? OutputPath { get; private set; }

    public void RecordTelemetry(WgcCaptureTelemetry telemetry)
    {
        lock (_gate)
        {
            _lastTelemetry = telemetry;
            if (telemetry.FramesReadBack > _lastReadbackCount && telemetry.LastReadbackDuration > TimeSpan.Zero)
            {
                _readbackMs.Add(telemetry.LastReadbackDuration.TotalMilliseconds);
                _lastReadbackCount = telemetry.FramesReadBack;
            }
        }
    }

    public void RecordRegionChange(RegionChange change, TimeSpan processingTime)
    {
        lock (_gate)
        {
            _regionChangeScores.Add(Math.Clamp(change.ChangeScore, 0, 1));
            _regionChangeProcessingUs.Add(Math.Max(0, processingTime.TotalMicroseconds));
            if (change.IsMeaningful)
                _meaningfulRegionChanges++;
        }
    }

    public void SampleProcess()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var now = DateTimeOffset.UtcNow;
        var totalCpu = process.TotalProcessorTime;
        var elapsed = now - _previousProcessSampleAt;
        var cpuDelta = totalCpu - _previousCpu;
        var normalizedCpu = elapsed.TotalMilliseconds > 0
            ? Math.Max(0, cpuDelta.TotalMilliseconds / elapsed.TotalMilliseconds / Math.Max(1, Environment.ProcessorCount) * 100.0)
            : 0;

        var sample = new ProcessMetricSample(
            now,
            normalizedCpu,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false));

        lock (_gate)
            _processSamples.Add(sample);

        _previousCpu = totalCpu;
        _previousProcessSampleAt = now;
    }

    public Task<string?> CompleteAsync(WgcCaptureEndReason endReason, Exception? error = null)
    {
        lock (_gate)
        {
            if (_completionTask is not null)
                return _completionTask;

            var report = BuildReportLocked(endReason, error);
            _completionTask = WriteReportAsync(report);
            return _completionTask;
        }
    }

    private CaptureBenchmarkReport BuildReportLocked(WgcCaptureEndReason endReason, Exception? error)
    {
        var readbacks = _readbackMs.Order().ToArray();
        var changeScores = _regionChangeScores.Order().ToArray();
        var changeProcessingUs = _regionChangeProcessingUs.Order().ToArray();
        var process = _processSamples.ToArray();
        var final = _lastTelemetry;
        var endedAt = DateTimeOffset.UtcNow;
        var duration = endedAt - _startedAt;

        return new CaptureBenchmarkReport(
            SchemaVersion: 4,
            AppVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
            StartedAtUtc: _startedAt,
            EndedAtUtc: endedAt,
            DurationSeconds: duration.TotalSeconds,
            EndReason: endReason.ToString(),
            Error: error is null ? null : $"{error.GetType().Name}: {error.Message}",
            Machine: new MachineInfo(
                Environment.OSVersion.VersionString,
                Environment.ProcessorCount,
                Environment.Is64BitOperatingSystem,
                Environment.Is64BitProcess,
                GCSettingsServer: System.Runtime.GCSettings.IsServerGC),
            Window: new WindowInfo(
                _window.ProcessName,
                _window.WindowTitle,
                $"0x{_window.Hwnd:X}",
                _window.Width,
                _window.Height),
            CaptureOptions: _options,
            Capture: final,
            Readback: new ReadbackStatistics(
                Count: readbacks.Length,
                AverageMs: Average(readbacks),
                P50Ms: Percentile(readbacks, 0.50),
                P95Ms: Percentile(readbacks, 0.95),
                P99Ms: Percentile(readbacks, 0.99),
                MaxMs: readbacks.Length == 0 ? 0 : readbacks[^1]),
            RegionChangeConfiguration: _changeConfiguration,
            RegionChange: BuildRegionChangeStatistics(
                changeScores,
                changeProcessingUs,
                _meaningfulRegionChanges,
                duration),
            Process: BuildProcessStatistics(process),
            SteadyStateProcess: BuildProcessStatistics(
                process.Where(x => x.AtUtc - _startedAt >= TimeSpan.FromSeconds(15)).ToArray()),
            SteadyStateWarmupExcludedSeconds: 15,
            Samples: process,
            Privacy: "AstralTFT process metrics + TFT window metadata only; no TFT process memory is read.");
    }

    private static RegionChangeStatistics BuildRegionChangeStatistics(
        double[] scores,
        double[] processingUs,
        long meaningfulCount,
        TimeSpan duration)
    {
        var count = scores.Length;
        var meaningful = Math.Clamp(meaningfulCount, 0, count);
        var baselineFrames = count > 0 ? 1 : 0;
        var meaningfulExcludingBaseline = Math.Max(0, meaningful - baselineFrames);
        var comparableFrames = Math.Max(0, count - baselineFrames);
        var meaningfulPercent = comparableFrames == 0
            ? 0
            : meaningfulExcludingBaseline * 100.0 / comparableFrames;

        return new RegionChangeStatistics(
            Count: count,
            BaselineFrames: baselineFrames,
            MeaningfulCount: meaningful,
            MeaningfulCountExcludingBaseline: meaningfulExcludingBaseline,
            SuppressedCountExcludingBaseline: Math.Max(0, comparableFrames - meaningfulExcludingBaseline),
            MeaningfulPercentExcludingBaseline: meaningfulPercent,
            MeaningfulChangesPerMinute: duration.TotalMinutes <= 0
                ? 0
                : meaningfulExcludingBaseline / duration.TotalMinutes,
            AverageScore: Average(scores),
            P95Score: Percentile(scores, 0.95),
            P99Score: Percentile(scores, 0.99),
            MaxScore: scores.Length == 0 ? 0 : scores[^1],
            AverageProcessingUs: Average(processingUs),
            P95ProcessingUs: Percentile(processingUs, 0.95),
            P99ProcessingUs: Percentile(processingUs, 0.99),
            MaxProcessingUs: processingUs.Length == 0 ? 0 : processingUs[^1]);
    }

    private static ProcessStatistics BuildProcessStatistics(ProcessMetricSample[] process)
    {
        return new ProcessStatistics(
            SampleCount: process.Length,
            AverageCpuPercent: process.Length == 0 ? 0 : process.Average(x => x.CpuPercent),
            P95CpuPercent: Percentile(process.Select(x => x.CpuPercent).Order().ToArray(), 0.95),
            MaxCpuPercent: process.Length == 0 ? 0 : process.Max(x => x.CpuPercent),
            AverageWorkingSetMb: process.Length == 0 ? 0 : process.Average(x => x.WorkingSetBytes) / 1024d / 1024d,
            MaxWorkingSetMb: process.Length == 0 ? 0 : process.Max(x => x.WorkingSetBytes) / 1024d / 1024d,
            AverageManagedHeapMb: process.Length == 0 ? 0 : process.Average(x => x.ManagedHeapBytes) / 1024d / 1024d,
            MaxManagedHeapMb: process.Length == 0 ? 0 : process.Max(x => x.ManagedHeapBytes) / 1024d / 1024d);
    }

    private async Task<string?> WriteReportAsync(CaptureBenchmarkReport report)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AstralTFT",
            "Diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"capture-benchmark-{_startedAt:yyyyMMdd-HHmmss}.json");

        // Write atomically. A crash or forced shutdown while serializing must not
        // leave a valid-looking but truncated benchmark JSON behind.
        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, report, new JsonSerializerOptions
                {
                    WriteIndented = true
                }).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
            OutputPath = path;
            return path;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // Best-effort cleanup only. The completed report path is authoritative.
            }
        }
    }

    private static double Average(double[] sorted) => sorted.Length == 0 ? 0 : sorted.Average();

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        percentile = Math.Clamp(percentile, 0, 1);
        var position = (sorted.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        var weight = position - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }

    internal sealed record ProcessMetricSample(
        DateTimeOffset AtUtc,
        double CpuPercent,
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long ManagedHeapBytes);

    private sealed record CaptureBenchmarkReport(
        int SchemaVersion,
        string AppVersion,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        double DurationSeconds,
        string EndReason,
        string? Error,
        MachineInfo Machine,
        WindowInfo Window,
        WgcCaptureOptions CaptureOptions,
        WgcCaptureTelemetry? Capture,
        ReadbackStatistics Readback,
        RegionChangeBenchmarkConfiguration? RegionChangeConfiguration,
        RegionChangeStatistics RegionChange,
        ProcessStatistics Process,
        ProcessStatistics SteadyStateProcess,
        int SteadyStateWarmupExcludedSeconds,
        IReadOnlyList<ProcessMetricSample> Samples,
        string Privacy);

    private sealed record MachineInfo(
        string OsVersion,
        int LogicalProcessorCount,
        bool Is64BitOperatingSystem,
        bool Is64BitProcess,
        bool GCSettingsServer);

    private sealed record WindowInfo(
        string ProcessName,
        string WindowTitle,
        string Hwnd,
        int Width,
        int Height);

    private sealed record ReadbackStatistics(
        int Count,
        double AverageMs,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double MaxMs);

    private sealed record RegionChangeStatistics(
        int Count,
        int BaselineFrames,
        long MeaningfulCount,
        long MeaningfulCountExcludingBaseline,
        long SuppressedCountExcludingBaseline,
        double MeaningfulPercentExcludingBaseline,
        double MeaningfulChangesPerMinute,
        double AverageScore,
        double P95Score,
        double P99Score,
        double MaxScore,
        double AverageProcessingUs,
        double P95ProcessingUs,
        double P99ProcessingUs,
        double MaxProcessingUs);

    private sealed record ProcessStatistics(
        int SampleCount,
        double AverageCpuPercent,
        double P95CpuPercent,
        double MaxCpuPercent,
        double AverageWorkingSetMb,
        double MaxWorkingSetMb,
        double AverageManagedHeapMb,
        double MaxManagedHeapMb);
}
