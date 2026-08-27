namespace AstralTFT.Capture.Windows;

/// <summary>
/// Conservative defaults for the first real-machine benchmark. Capture itself may
/// run at the desktop refresh rate, but CPU readback is deliberately throttled so
/// the diagnostic build cannot accidentally compete with TFT.
/// </summary>
public sealed record WgcCaptureOptions(
    int MaxCpuReadbacksPerSecond = 10,
    int FramePoolBufferCount = 2,
    bool CaptureCursor = false,
    bool AllowWarpFallback = false)
{
    public WgcCaptureOptions Validate()
    {
        if (MaxCpuReadbacksPerSecond is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(MaxCpuReadbacksPerSecond));
        if (FramePoolBufferCount is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(FramePoolBufferCount));
        return this;
    }
}
