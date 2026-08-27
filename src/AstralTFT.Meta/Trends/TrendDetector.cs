namespace AstralTFT.Meta.Trends;

public sealed record TrendPoint(
    DateTimeOffset At,
    double Value,
    long SampleSize,
    double SourceQuality);

public enum TrendDirection
{
    Falling = -1,
    Flat = 0,
    Rising = 1
}

public sealed record TrendSignal(
    TrendDirection Direction,
    double Strength,
    double Confidence,
    double RecentValue,
    double BaselineValue,
    string Reason);

/// <summary>
/// Detects emerging movement without treating tiny samples as meta breakthroughs.
/// Uses recency/sample/quality-weighted linear regression over normalized time.
/// </summary>
public static class TrendDetector
{
    public static TrendSignal Detect(IReadOnlyList<TrendPoint> points, DateTimeOffset now)
    {
        if (points.Count < 3)
            return new(TrendDirection.Flat, 0, 0, points.LastOrDefault()?.Value ?? 0, points.FirstOrDefault()?.Value ?? 0,
                "Insufficient observations for a reliable trend.");

        var ordered = points.OrderBy(x => x.At).ToArray();
        var minAt = ordered[0].At;
        var maxHours = Math.Max(1e-6, (ordered[^1].At - minAt).TotalHours);

        var weighted = ordered.Select(p =>
        {
            var x = (p.At - minAt).TotalHours / maxHours;
            var ageHours = Math.Max(0, (now - p.At).TotalHours);
            var recency = Math.Exp(-ageHours / 18.0);
            var sample = 1.0 - Math.Exp(-Math.Max(0, p.SampleSize) / 5_000.0);
            var quality = Math.Clamp(p.SourceQuality, 0, 1);
            var w = Math.Max(1e-6, recency * sample * quality);
            return (x, y: p.Value, w);
        }).ToArray();

        var weightSum = weighted.Sum(p => p.w);
        var meanX = weighted.Sum(p => p.x * p.w) / weightSum;
        var meanY = weighted.Sum(p => p.y * p.w) / weightSum;
        var numerator = weighted.Sum(p => p.w * (p.x - meanX) * (p.y - meanY));
        var denominator = weighted.Sum(p => p.w * Math.Pow(p.x - meanX, 2));
        var slope = denominator <= 1e-9 ? 0 : numerator / denominator;

        var baseline = ordered.Take(Math.Max(1, ordered.Length / 2)).Average(x => x.Value);
        var recent = ordered.Skip(ordered.Length / 2).Average(x => x.Value);
        var scale = Math.Max(0.01, Math.Abs(baseline));
        var relativeMovement = (recent - baseline) / scale;
        var strength = Math.Clamp(Math.Abs(relativeMovement) * 2.5, 0, 1);

        var effectiveSamples = ordered.Sum(x => Math.Max(0L, x.SampleSize));
        var sampleConfidence = 1.0 - Math.Exp(-effectiveSamples / 25_000.0);
        var qualityConfidence = ordered.Average(x => Math.Clamp(x.SourceQuality, 0, 1));
        var confidence = Math.Clamp(sampleConfidence * qualityConfidence, 0, 1);

        var direction = Math.Abs(slope) < 0.005 || strength < 0.05
            ? TrendDirection.Flat
            : slope > 0 ? TrendDirection.Rising : TrendDirection.Falling;

        return new TrendSignal(
            direction,
            strength,
            confidence,
            recent,
            baseline,
            $"Weighted slope {slope:F4}; relative recent-vs-baseline movement {relativeMovement:P1}.");
    }
}
