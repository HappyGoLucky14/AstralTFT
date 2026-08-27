using System.Buffers;
using AstralTFT.Capture.Abstractions;
using AstralTFT.Core.Models;

namespace AstralTFT.Capture.Recognition;

/// <summary>
/// Stable, detector-facing snapshot of one TFT UI region. Implementations own any
/// backing buffers/resources and must remain valid until disposed by the pipeline.
/// Capture frames themselves should never be held by slow recognition workers.
/// </summary>
public interface IRegionSnapshot : IDisposable
{
    string RegionId { get; }
    long FrameSequence { get; }
    DateTimeOffset CapturedAt { get; }
    int Width { get; }
    int Height { get; }
}

public interface IRegionSnapshotFactory
{
    IRegionSnapshot Create(CapturedFrame frame, RegionOfInterest region);
}

/// <summary>
/// CPU-visible BGRA snapshot used by fallback recognisers and deterministic tests.
/// Production GPU recognisers may use a different snapshot implementation.
/// </summary>
public sealed class Bgra32RegionSnapshot : IRegionSnapshot
{
    private byte[]? _pixels;
    private readonly int _length;
    private readonly ArrayPool<byte>? _pool;

    public Bgra32RegionSnapshot(
        string regionId,
        long frameSequence,
        DateTimeOffset capturedAt,
        int width,
        int height,
        int stride,
        byte[] pixels)
        : this(regionId, frameSequence, capturedAt, width, height, stride, pixels, pool: null)
    {
    }

    internal Bgra32RegionSnapshot(
        string regionId,
        long frameSequence,
        DateTimeOffset capturedAt,
        int width,
        int height,
        int stride,
        byte[] pixels,
        ArrayPool<byte>? pool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < width * 4) throw new ArgumentOutOfRangeException(nameof(stride));
        var length = checked(stride * height);
        if (pixels.Length < length)
            throw new ArgumentException("Pixel buffer is smaller than stride × height.", nameof(pixels));

        RegionId = regionId;
        FrameSequence = frameSequence;
        CapturedAt = capturedAt;
        Width = width;
        Height = height;
        Stride = stride;
        _pixels = pixels;
        _length = length;
        _pool = pool;
    }

    public string RegionId { get; }
    public long FrameSequence { get; }
    public DateTimeOffset CapturedAt { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public ReadOnlyMemory<byte> Pixels => _pixels is { } pixels
        ? new ReadOnlyMemory<byte>(pixels, 0, _length)
        : throw new ObjectDisposedException(nameof(Bgra32RegionSnapshot));

    public void Dispose()
    {
        var pixels = Interlocked.Exchange(ref _pixels, null);
        if (pixels is not null && _pool is not null)
            _pool.Return(pixels);
    }
}

public sealed record RecognitionObservation(
    string Kind,
    string Key,
    object? Value,
    Confidence Confidence,
    string Source,
    DateTimeOffset ObservedAt,
    string RegionId,
    long FrameSequence,
    string? EvidenceHash = null);

public enum RecognitionBatchStatus
{
    Success,
    NoObservation,
    LowConfidence,
    Failed,
    SkippedStale,
    SkippedUnhealthy
}

public sealed record RecognitionBatch(
    string DetectorId,
    string RegionId,
    long FrameSequence,
    DateTimeOffset CapturedAt,
    DateTimeOffset CompletedAt,
    RecognitionBatchStatus Status,
    IReadOnlyList<RecognitionObservation> Observations,
    string? Diagnostic = null)
{
    public static RecognitionBatch Empty(
        string detectorId,
        IRegionSnapshot snapshot,
        DateTimeOffset completedAt,
        RecognitionBatchStatus status = RecognitionBatchStatus.NoObservation,
        string? diagnostic = null) => new(
            detectorId,
            snapshot.RegionId,
            snapshot.FrameSequence,
            snapshot.CapturedAt,
            completedAt,
            status,
            Array.Empty<RecognitionObservation>(),
            diagnostic);
}

public sealed record RecognitionDetectorDescriptor(
    string Id,
    IReadOnlySet<string> SupportedRegionIds,
    TimeSpan StaleAfter,
    int MaxConsecutiveFailures = 4,
    TimeSpan FailureCooldown = default)
{
    public TimeSpan EffectiveFailureCooldown => FailureCooldown <= TimeSpan.Zero
        ? TimeSpan.FromSeconds(10)
        : FailureCooldown;
}

public interface IRegionObservationDetector
{
    RecognitionDetectorDescriptor Descriptor { get; }

    ValueTask<RecognitionBatch> DetectAsync(
        IRegionSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
