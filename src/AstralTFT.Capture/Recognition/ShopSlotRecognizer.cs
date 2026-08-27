using AstralTFT.Capture.Abstractions;

namespace AstralTFT.Capture.Recognition;

public enum ShopSlotOccupancy
{
    Unknown,
    Empty,
    Unit
}

public sealed record ShopSlotReading(
    int SlotIndex,
    ShopSlotOccupancy Occupancy,
    int CostTier,
    double PresenceConfidence,
    ulong VisualHash,
    RegionOfInterest Region);

public sealed record ShopHudObservation(
    bool IsVisible,
    bool SupportsHold,
    double Confidence,
    int TopBorderMatches,
    int SeparatorMatches);

public sealed record ShopRecognitionResult(
    IReadOnlyList<ShopSlotReading> Slots,
    ShopHudObservation Hud,
    bool IsShopHudVisible,
    int KnownSlotCount,
    int UnitSlotCount,
    TimeSpan ProcessingTime);

/// <summary>
/// First shop recognizer gate. It intentionally does not guess champion identity yet:
/// it locates all five shop cards, distinguishes bought/empty slots from units, infers
/// the cost-tier color bar, and records a compact visual hash for the next template
/// matching stage.
///
/// Geometry was calibrated against the current 1920x1080 Set 18 borderless TFT HUD.
/// The coordinates are normalized inside AstralTFT's existing shop-band readback ROI,
/// so the profile scales with supported resolutions instead of hard-coding 1080p.
/// </summary>
public sealed class ShopSlotRecognizer
{
    private readonly record struct NormalizedSlot(double X, double Y, double Width, double Height);
    private readonly record struct FrameBandEvidence(
        double ChromaticRatio,
        double NeutralRatio,
        double MeanLuma);

    private const double ActiveTopBorderRatio = 0.28;
    private const double ActiveSeparatorRatio = 0.18;
    private const double HoldTopBorderRatio = 0.24;
    private const double HoldSeparatorRatio = 0.14;
    private const double MutedFrameMinimumContrast = 6;

    // Calibrated from a real 1920x1080 Set 18 frame. Reference readback ROI:
    // 1152x239 at full-frame x=.20, y=.77, w=.60, h=.22.
    private static readonly NormalizedSlot[] Slots =
    [
        new(174d / 1152d, 90d / 239d, 174d / 1152d, 143d / 239d),
        new(358d / 1152d, 86d / 239d, 192d / 1152d, 146d / 239d),
        new(561d / 1152d, 86d / 239d, 187d / 1152d, 146d / 239d),
        new(759d / 1152d, 86d / 239d, 189d / 1152d, 146d / 239d),
        new(959d / 1152d, 86d / 239d, 193d / 1152d, 146d / 239d)
    ];

    public static IReadOnlyList<RegionOfInterest> ProjectSlots(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var projected = new RegionOfInterest[Slots.Length];
        for (var i = 0; i < Slots.Length; i++)
        {
            var slot = Slots[i];
            var x = Math.Clamp((int)Math.Round(slot.X * width), 0, width - 1);
            var y = Math.Clamp((int)Math.Round(slot.Y * height), 0, height - 1);
            var right = Math.Clamp((int)Math.Round((slot.X + slot.Width) * width), x + 1, width);
            var bottom = Math.Clamp((int)Math.Round((slot.Y + slot.Height) * height), y + 1, height);

            projected[i] = new RegionOfInterest(
                $"shop-slot-{i + 1}",
                x,
                y,
                right - x,
                bottom - y);
        }

        return projected;
    }

    public ShopHudObservation CheckHud(Bgra32FrameBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Validate();

        var regions = ProjectSlots(buffer.Width, buffer.Height);
        var pixels = buffer.Pixels.Span;

        var activeTopBorderMatches = 0;
        var holdTopBorderMatches = 0;
        foreach (var region in regions)
        {
            var stripTop = Math.Clamp(region.Y - 2, 0, buffer.Height - 1);
            var stripBottom = Math.Clamp(region.Y + 5, stripTop + 1, buffer.Height);
            var evidence = MeasureFrameBandEvidence(
                pixels,
                buffer.Stride,
                region.X,
                stripTop,
                region.Width,
                stripBottom - stripTop);

            var referenceTop = Math.Clamp(stripBottom + 2, 0, buffer.Height - 1);
            var referenceBottom = Math.Clamp(referenceTop + 5, referenceTop + 1, buffer.Height);
            var referenceLuma = MeasureMeanLuma(
                pixels,
                buffer.Stride,
                region.X,
                referenceTop,
                region.Width,
                referenceBottom - referenceTop);
            var hasFrameContrast = HasFrameContrast(evidence.MeanLuma, referenceLuma);

            if (hasFrameContrast && evidence.ChromaticRatio >= ActiveTopBorderRatio)
                activeTopBorderMatches++;

            if (hasFrameContrast &&
                (evidence.ChromaticRatio >= HoldTopBorderRatio ||
                 evidence.NeutralRatio >= HoldTopBorderRatio))
            {
                holdTopBorderMatches++;
            }
        }

        var activeSeparatorMatches = 0;
        var holdSeparatorMatches = 0;
        for (var i = 0; i < regions.Count - 1; i++)
        {
            var left = regions[i];
            var right = regions[i + 1];

            var gapLeft = left.X + left.Width;
            var gapRight = right.X;
            if (gapRight <= gapLeft)
                continue;

            var stripTop = Math.Max(left.Y, right.Y) + 8;
            var stripBottom = Math.Min(left.Y + left.Height, right.Y + right.Height) - 8;
            if (stripBottom <= stripTop)
                continue;

            var evidence = MeasureFrameBandEvidence(
                pixels,
                buffer.Stride,
                gapLeft,
                stripTop,
                gapRight - gapLeft,
                stripBottom - stripTop);

            var referenceWidth = Math.Min(4, Math.Min(left.Width, right.Width));
            var leftReferenceLuma = MeasureMeanLuma(
                pixels,
                buffer.Stride,
                gapLeft - referenceWidth,
                stripTop,
                referenceWidth,
                stripBottom - stripTop);
            var rightReferenceLuma = MeasureMeanLuma(
                pixels,
                buffer.Stride,
                gapRight,
                stripTop,
                referenceWidth,
                stripBottom - stripTop);
            var referenceLuma = (leftReferenceLuma + rightReferenceLuma) / 2;
            var hasFrameContrast = HasFrameContrast(evidence.MeanLuma, referenceLuma);

            if (hasFrameContrast && evidence.ChromaticRatio >= ActiveSeparatorRatio)
                activeSeparatorMatches++;

            if (hasFrameContrast &&
                (evidence.ChromaticRatio >= HoldSeparatorRatio ||
                 evidence.NeutralRatio >= HoldSeparatorRatio))
            {
                holdSeparatorMatches++;
            }
        }

        var activeConfidence =
            (activeTopBorderMatches / 5d) * 0.55 +
            (activeSeparatorMatches / 4d) * 0.45;

        var isVisible =
            activeTopBorderMatches >= 4 &&
            activeSeparatorMatches >= 3;

        // Once a real shop has been confirmed, muted/greyed card states and short
        // round-transition effects are allowed to keep the HUD alive without
        // authorizing a fresh slot read. This is deliberately weaker than the
        // initial activation threshold and is only used as temporal hysteresis.
        var supportsHold =
            (holdTopBorderMatches >= 3 && holdSeparatorMatches >= 2) ||
            (holdTopBorderMatches >= 4 && holdSeparatorMatches >= 1);

        return new ShopHudObservation(
            IsVisible: isVisible,
            SupportsHold: supportsHold,
            Confidence: activeConfidence,
            TopBorderMatches: activeTopBorderMatches,
            SeparatorMatches: activeSeparatorMatches);
    }

    public ShopRecognitionResult Recognize(Bgra32FrameBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Validate();

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var hud = CheckHud(buffer);
        var regions = ProjectSlots(buffer.Width, buffer.Height);
        var readings = new ShopSlotReading[regions.Count];

        for (var i = 0; i < regions.Count; i++)
            readings[i] = AnalyzeSlot(buffer, regions[i], i + 1);

        var knownSlotCount = readings.Count(IsStructurallyKnown);
        var unitSlotCount = readings.Count(x => x.Occupancy == ShopSlotOccupancy.Unit);

        return new ShopRecognitionResult(
            readings,
            Hud: hud,
            IsShopHudVisible: hud.IsVisible,
            KnownSlotCount: knownSlotCount,
            UnitSlotCount: unitSlotCount,
            ProcessingTime: System.Diagnostics.Stopwatch.GetElapsedTime(started));
    }

    private static FrameBandEvidence MeasureFrameBandEvidence(
        ReadOnlySpan<byte> pixels,
        int stride,
        int x,
        int y,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            return default;

        long chromaticMatches = 0;
        long neutralMatches = 0;
        long count = 0;
        double lumaSum = 0;

        for (var yy = y; yy < y + height; yy++)
        {
            for (var xx = x; xx < x + width; xx++)
            {
                var offset = checked(yy * stride + xx * 4);
                var b = pixels[offset];
                var g = pixels[offset + 1];
                var r = pixels[offset + 2];
                var max = Math.Max(r, Math.Max(g, b));

                var min = Math.Min(r, Math.Min(g, b));
                var chroma = max - min;
                var luma = r * 0.2126 + g * 0.7152 + b * 0.0722;

                // Only chromatic TFT shop chrome may activate recognition. Neutral
                // pixels are weaker evidence reserved for temporal hold and require
                // local contrast at the caller so flat scenery cannot match.
                var darkCyanFrame =
                    max <= 80 &&
                    r <= 55 &&
                    chroma >= 6 &&
                    g >= r &&
                    b >= r &&
                    g - r >= 2 &&
                    b - r >= 2;

                var darkNeutralFrame =
                    luma is >= 16 and <= 72 &&
                    max <= 86 &&
                    chroma <= 22;

                if (darkCyanFrame)
                    chromaticMatches++;
                if (darkNeutralFrame)
                    neutralMatches++;

                lumaSum += luma;
                count++;
            }
        }

        return count == 0
            ? default
            : new FrameBandEvidence(
                chromaticMatches / (double)count,
                neutralMatches / (double)count,
                lumaSum / count);
    }

    private static bool HasFrameContrast(double frameLuma, double referenceLuma)
    {
        return Math.Abs(frameLuma - referenceLuma) >= MutedFrameMinimumContrast;
    }

    private static double MeasureMeanLuma(
        ReadOnlySpan<byte> pixels,
        int stride,
        int x,
        int y,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            return 0;

        double sum = 0;
        long count = 0;

        for (var yy = y; yy < y + height; yy++)
        {
            for (var xx = x; xx < x + width; xx++)
            {
                sum += LumaAt(pixels, stride, xx, yy);
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    private static bool IsStructurallyKnown(ShopSlotReading slot)
    {
        return slot.Occupancy switch
        {
            ShopSlotOccupancy.Empty => true,
            ShopSlotOccupancy.Unit => slot.CostTier is >= 1 and <= 5,
            _ => false
        };
    }

    private static ShopSlotReading AnalyzeSlot(
        Bgra32FrameBuffer buffer,
        RegionOfInterest region,
        int slotIndex)
    {
        var presence = MeasurePresence(buffer, region);

        ShopSlotOccupancy occupancy;
        double confidence;

        if (presence.StandardDeviation < 12 &&
            presence.EdgeEnergy < 5 &&
            presence.MeanLuma < 25)
        {
            occupancy = ShopSlotOccupancy.Empty;
            var strongest = Math.Max(
                presence.StandardDeviation / 12d,
                Math.Max(presence.EdgeEnergy / 5d, presence.MeanLuma / 25d));
            confidence = Math.Clamp(1d - strongest * 0.45d, 0.55d, 0.99d);
        }
        else if (presence.StandardDeviation >= 18 ||
                 presence.EdgeEnergy >= 7 ||
                 presence.MeanLuma >= 30)
        {
            occupancy = ShopSlotOccupancy.Unit;
            var evidence = Math.Max(
                presence.StandardDeviation / 45d,
                Math.Max(presence.EdgeEnergy / 18d, presence.MeanLuma / 80d));
            confidence = Math.Clamp(0.55d + evidence * 0.30d, 0.60d, 0.99d);
        }
        else
        {
            occupancy = ShopSlotOccupancy.Unknown;
            confidence = 0.50d;
        }

        var costTier = occupancy == ShopSlotOccupancy.Unit
            ? InferCostTier(buffer, region)
            : 0;

        return new ShopSlotReading(
            slotIndex,
            occupancy,
            costTier,
            confidence,
            ComputeAverageHash(buffer, region),
            region);
    }

    private static (double MeanLuma, double StandardDeviation, double EdgeEnergy) MeasurePresence(
        Bgra32FrameBuffer buffer,
        RegionOfInterest region)
    {
        var x0 = region.X + (int)Math.Round(region.Width * 0.08);
        var x1 = region.X + (int)Math.Round(region.Width * 0.92);
        var y0 = region.Y + (int)Math.Round(region.Height * 0.08);
        var y1 = region.Y + (int)Math.Round(region.Height * 0.72);

        x1 = Math.Clamp(x1, x0 + 1, region.X + region.Width);
        y1 = Math.Clamp(y1, y0 + 1, region.Y + region.Height);

        var pixels = buffer.Pixels.Span;
        double sum = 0;
        double sumSquares = 0;
        double edgeSum = 0;
        long count = 0;
        long edgeCount = 0;

        for (var y = y0; y < y1; y++)
        {
            double previous = -1;
            for (var x = x0; x < x1; x++)
            {
                var luma = LumaAt(pixels, buffer.Stride, x, y);
                sum += luma;
                sumSquares += luma * luma;
                count++;

                if (previous >= 0)
                {
                    edgeSum += Math.Abs(luma - previous);
                    edgeCount++;
                }

                if (y > y0)
                {
                    var above = LumaAt(pixels, buffer.Stride, x, y - 1);
                    edgeSum += Math.Abs(luma - above);
                    edgeCount++;
                }

                previous = luma;
            }
        }

        if (count == 0)
            return (0, 0, 0);

        var mean = sum / count;
        var variance = Math.Max(0, sumSquares / count - mean * mean);
        return (
            mean,
            Math.Sqrt(variance),
            edgeCount == 0 ? 0 : edgeSum / edgeCount);
    }

    private static int InferCostTier(Bgra32FrameBuffer buffer, RegionOfInterest region)
    {
        // The Set 18 cost color is the dominant background of the lower name bar.
        // The previous sparse-point sampler could land on champion-name glyphs, the
        // coin/cost icon, or decorative pixels and occasionally turn a 5-cost into
        // a 4- or 1-cost. Use the whole lower band and take a robust channel median
        // after rejecting near-black borders and bright foreground glyphs.
        const int maxSamples = 1024;
        Span<int> reds = stackalloc int[maxSamples];
        Span<int> greens = stackalloc int[maxSamples];
        Span<int> blues = stackalloc int[maxSamples];
        var count = 0;
        var pixels = buffer.Pixels.Span;

        var x0 = region.X + Math.Max(1, (int)Math.Round(region.Width * 0.02));
        var x1 = region.X + Math.Max(2, (int)Math.Round(region.Width * 0.98));
        var y0 = region.Y + Math.Max(1, (int)Math.Round(region.Height * 0.84));
        var y1 = region.Y + Math.Max(2, (int)Math.Round(region.Height * 0.98));

        x1 = Math.Clamp(x1, x0 + 1, region.X + region.Width);
        y1 = Math.Clamp(y1, y0 + 1, region.Y + region.Height);

        for (var y = y0; y < y1 && count < maxSamples; y += 2)
        {
            for (var x = x0; x < x1 && count < maxSamples; x += 2)
            {
                var offset = checked(y * buffer.Stride + x * 4);
                var b = pixels[offset];
                var g = pixels[offset + 1];
                var r = pixels[offset + 2];
                var max = Math.Max(r, Math.Max(g, b));

                if (max < 10 || max > 160)
                    continue;

                reds[count] = r;
                greens[count] = g;
                blues[count] = b;
                count++;
            }
        }

        if (count < 12)
            return 0;

        reds[..count].Sort();
        greens[..count].Sort();
        blues[..count].Sort();

        var rMedian = Median(reds[..count]);
        var gMedian = Median(greens[..count]);
        var bMedian = Median(blues[..count]);
        var (hue, saturation, _) = ToHsv(rMedian, gMedian, bMedian);

        if (hue is >= 125 and <= 190 && saturation >= 0.20)
            return 2;

        if (hue is >= 195 and <= 260)
        {
            if (bMedian >= 60 && bMedian - rMedian >= 28)
                return 3;
            return 1;
        }

        if (hue is > 260 and <= 340 && saturation >= 0.18)
            return 4;

        if ((hue is >= 15 and <= 90 && saturation >= 0.18) ||
            (rMedian > gMedian && gMedian > bMedian && rMedian - bMedian >= 18))
        {
            return 5;
        }

        return 0;
    }

    private static ulong ComputeAverageHash(Bgra32FrameBuffer buffer, RegionOfInterest region)
    {
        Span<double> samples = stackalloc double[64];
        var pixels = buffer.Pixels.Span;
        var sum = 0d;

        // Avoid card borders and the name bar. The hash is evidence only for now;
        // the next stage will compare it against a full CommunityDragon template set.
        var left = region.X + (int)Math.Round(region.Width * 0.18);
        var top = region.Y + (int)Math.Round(region.Height * 0.08);
        var width = Math.Max(1, (int)Math.Round(region.Width * 0.74));
        var height = Math.Max(1, (int)Math.Round(region.Height * 0.62));

        var index = 0;
        for (var gy = 0; gy < 8; gy++)
        {
            for (var gx = 0; gx < 8; gx++)
            {
                var x = Math.Clamp(
                    left + (int)Math.Round((gx + 0.5) * width / 8d),
                    region.X,
                    region.X + region.Width - 1);
                var y = Math.Clamp(
                    top + (int)Math.Round((gy + 0.5) * height / 8d),
                    region.Y,
                    region.Y + region.Height - 1);

                var luma = LumaAt(pixels, buffer.Stride, x, y);
                samples[index++] = luma;
                sum += luma;
            }
        }

        var average = sum / samples.Length;
        ulong hash = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            if (samples[i] >= average)
                hash |= 1UL << i;
        }

        return hash;
    }

    private static int Median(Span<int> sorted)
    {
        if (sorted.Length == 0)
            return 0;

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static (double Hue, double Saturation, double Value) ToHsv(int r8, int g8, int b8)
    {
        var r = r8 / 255d;
        var g = g8 / 255d;
        var b = b8 / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta <= double.Epsilon)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = (60 * ((g - b) / delta) + 360) % 360;
        }
        else if (max == g)
        {
            hue = 60 * ((b - r) / delta) + 120;
        }
        else
        {
            hue = 60 * ((r - g) / delta) + 240;
        }

        var saturation = max <= double.Epsilon ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static double LumaAt(ReadOnlySpan<byte> pixels, int stride, int x, int y)
    {
        var offset = checked(y * stride + x * 4);
        var b = pixels[offset];
        var g = pixels[offset + 1];
        var r = pixels[offset + 2];
        return r * 0.2126 + g * 0.7152 + b * 0.0722;
    }
}
