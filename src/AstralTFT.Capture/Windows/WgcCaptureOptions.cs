using AstralTFT.Capture.Abstractions;

namespace AstralTFT.Capture.Windows;

/// <summary>
/// Normalized crop used by the hardware benchmark and, later, by region-specific
/// recognizers. Values are relative to the captured TFT client area.
/// </summary>
public sealed record WgcNormalizedRegion(
    string Id,
    double X,
    double Y,
    double Width,
    double Height)
{
    public WgcNormalizedRegion Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Region id is required.", nameof(Id));
        if (X is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(X));
        if (Y is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(Y));
        if (Width is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Height));
        if (X + Width > 1.000001) throw new ArgumentOutOfRangeException(nameof(Width), "Region extends beyond the frame.");
        if (Y + Height > 1.000001) throw new ArgumentOutOfRangeException(nameof(Height), "Region extends beyond the frame.");
        return this;
    }

    public RegionOfInterest Project(int frameWidth, int frameHeight)
    {
        Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);

        var left = Math.Clamp((int)Math.Floor(X * frameWidth), 0, frameWidth - 1);
        var top = Math.Clamp((int)Math.Floor(Y * frameHeight), 0, frameHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling((X + Width) * frameWidth), left + 1, frameWidth);
        var bottom = Math.Clamp((int)Math.Ceiling((Y + Height) * frameHeight), top + 1, frameHeight);
        return new RegionOfInterest(Id, left, top, right - left, bottom - top);
    }
}

/// <summary>
/// Capture may run at the desktop refresh rate while CPU-visible copies are bounded.
/// CpuReadbackRegion=null preserves the full-frame benchmark path. Supplying a region
/// keeps the rest of the TFT frame GPU-only and copies just that ROI to system memory.
/// </summary>
public sealed record WgcCaptureOptions(
    int MaxCpuReadbacksPerSecond = 10,
    int FramePoolBufferCount = 2,
    bool CaptureCursor = false,
    bool AllowWarpFallback = false,
    WgcNormalizedRegion? CpuReadbackRegion = null)
{
    public WgcCaptureOptions Validate()
    {
        if (MaxCpuReadbacksPerSecond is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(MaxCpuReadbacksPerSecond));
        if (FramePoolBufferCount is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(FramePoolBufferCount));
        CpuReadbackRegion?.Validate();
        return this;
    }
}
