using System.Buffers;
using AstralTFT.Capture.Abstractions;

namespace AstralTFT.Capture.Recognition;

/// <summary>
/// Diagnostic/fallback snapshot path for CPU-visible BGRA frames. Copies only the
/// changed ROI and rents the destination from ArrayPool to keep recognition bursts
/// from becoming a GC workload.
/// </summary>
public sealed class CpuBgraRegionSnapshotFactory : IRegionSnapshotFactory
{
    public IRegionSnapshot Create(CapturedFrame frame, RegionOfInterest region)
    {
        if (frame.NativeFrameHandle is not Bgra32FrameBuffer buffer)
            throw new InvalidOperationException(
                "CpuBgraRegionSnapshotFactory requires a Bgra32FrameBuffer. " +
                "Use a GPU-backed snapshot factory for production WGC frames.");

        buffer.Validate();
        var roi = Clamp(region, buffer.Width, buffer.Height);
        var stride = checked(roi.Width * 4);
        var required = checked(stride * roi.Height);
        var pool = ArrayPool<byte>.Shared;
        var pixels = pool.Rent(required);
        try
        {
            var source = buffer.Pixels.Span;
            var destination = pixels.AsSpan(0, required);

            for (var row = 0; row < roi.Height; row++)
            {
                var sourceOffset = checked((roi.Y + row) * buffer.Stride + roi.X * 4);
                var destinationOffset = checked(row * stride);
                source.Slice(sourceOffset, stride).CopyTo(destination.Slice(destinationOffset, stride));
            }

            var snapshot = new Bgra32RegionSnapshot(
                roi.Id,
                frame.Sequence,
                frame.CapturedAt,
                roi.Width,
                roi.Height,
                stride,
                pixels,
                pool);
            pixels = null!;
            return snapshot;
        }
        finally
        {
            if (pixels is not null)
                pool.Return(pixels);
        }
    }

    private static RegionOfInterest Clamp(RegionOfInterest region, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var x = Math.Clamp(region.X, 0, width - 1);
        var y = Math.Clamp(region.Y, 0, height - 1);
        var right = Math.Clamp(region.X + Math.Max(1, region.Width), x + 1, width);
        var bottom = Math.Clamp(region.Y + Math.Max(1, region.Height), y + 1, height);
        return region with { X = x, Y = y, Width = right - x, Height = bottom - y };
    }
}
