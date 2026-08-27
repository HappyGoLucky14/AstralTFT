using AstralTFT.Capture.Abstractions;

namespace AstralTFT.Capture.Regions;

/// <summary>
/// Low-cost CPU fallback for diagnostics and first-pass benchmarking. Samples a small luminance
/// grid rather than scanning every pixel. Production can replace this with a GPU ROI fingerprint
/// without changing the scheduler contract.
/// </summary>
public sealed class GridLumaRegionChangeDetector : IRegionChangeDetector
{
    private readonly Dictionary<string, byte[]> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _gridColumns;
    private readonly int _gridRows;
    private readonly double _meaningfulThreshold;

    public GridLumaRegionChangeDetector(
        int gridColumns = 16,
        int gridRows = 9,
        double meaningfulThreshold = 0.035)
    {
        if (gridColumns < 2) throw new ArgumentOutOfRangeException(nameof(gridColumns));
        if (gridRows < 2) throw new ArgumentOutOfRangeException(nameof(gridRows));
        if (meaningfulThreshold is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(meaningfulThreshold));

        _gridColumns = gridColumns;
        _gridRows = gridRows;
        _meaningfulThreshold = meaningfulThreshold;
    }

    public RegionChange Compare(CapturedFrame frame, RegionOfInterest region)
    {
        if (frame.NativeFrameHandle is not Bgra32FrameBuffer buffer)
            throw new InvalidOperationException(
                "GridLumaRegionChangeDetector requires a Bgra32FrameBuffer. " +
                "Use a GPU-backed IRegionChangeDetector for production WGC frames.");

        buffer.Validate();
        var roi = Clamp(region, buffer.Width, buffer.Height);
        var current = Sample(buffer, roi);

        if (!_previous.TryGetValue(region.Id, out var prior) || prior.Length != current.Length)
        {
            _previous[region.Id] = current;
            return new RegionChange(region.Id, 1.0, true);
        }

        long absoluteDifference = 0;
        for (var i = 0; i < current.Length; i++)
            absoluteDifference += Math.Abs(current[i] - prior[i]);

        var score = absoluteDifference / (255.0 * current.Length);
        _previous[region.Id] = current;
        return new RegionChange(region.Id, score, score >= _meaningfulThreshold);
    }

    private byte[] Sample(Bgra32FrameBuffer buffer, RegionOfInterest roi)
    {
        var result = new byte[_gridColumns * _gridRows];
        var span = buffer.Pixels.Span;
        var index = 0;

        for (var gy = 0; gy < _gridRows; gy++)
        {
            var y = roi.Y + Math.Min(roi.Height - 1,
                (int)Math.Round((gy + 0.5) * roi.Height / _gridRows - 0.5));

            for (var gx = 0; gx < _gridColumns; gx++)
            {
                var x = roi.X + Math.Min(roi.Width - 1,
                    (int)Math.Round((gx + 0.5) * roi.Width / _gridColumns - 0.5));

                var pixelOffset = checked((y * buffer.Stride) + (x * 4));
                var b = span[pixelOffset];
                var g = span[pixelOffset + 1];
                var r = span[pixelOffset + 2];

                // Integer approximation of Rec.709 luminance: 0.2126R + 0.7152G + 0.0722B.
                result[index++] = (byte)((54 * r + 183 * g + 19 * b) >> 8);
            }
        }

        return result;
    }

    private static RegionOfInterest Clamp(RegionOfInterest region, int width, int height)
    {
        var x = Math.Clamp(region.X, 0, width - 1);
        var y = Math.Clamp(region.Y, 0, height - 1);
        var right = Math.Clamp(region.X + Math.Max(1, region.Width), x + 1, width);
        var bottom = Math.Clamp(region.Y + Math.Max(1, region.Height), y + 1, height);
        return region with { X = x, Y = y, Width = right - x, Height = bottom - y };
    }
}
