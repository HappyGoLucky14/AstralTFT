namespace AstralTFT.Capture.Abstractions;

/// <summary>
/// Optional CPU-visible frame used by diagnostics/tests and as a fallback path.
/// Production Windows Graphics Capture is expected to remain GPU-backed until a detector
/// explicitly requests a CPU sample.
/// </summary>
public sealed record Bgra32FrameBuffer(
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> Pixels)
{
    public int RequiredByteLength => checked(Stride * Height);

    public void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        if (Stride < checked(Width * 4))
            throw new ArgumentOutOfRangeException(nameof(Stride), "BGRA32 stride must be at least width * 4.");
        if (Pixels.Length < RequiredByteLength)
            throw new ArgumentException("Pixel buffer is shorter than stride * height.", nameof(Pixels));
    }
}
