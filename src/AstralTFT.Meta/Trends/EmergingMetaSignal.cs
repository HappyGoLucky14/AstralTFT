namespace AstralTFT.Meta.Trends;

public enum EmergingMetaState
{
    Cooling = -1,
    Unclear = 0,
    Emerging = 1
}

public sealed record MetricTrendEvidence(
    string MetricId,
    TrendSignal Signal,
    bool HigherIsBetter,
    double Importance = 1.0);

public sealed record EmergingMetaSignal(
    EmergingMetaState State,
    double Strength,
    double Confidence,
    IReadOnlyList<MetricTrendEvidence> Evidence,
    string Reason);

/// <summary>
/// Combines several independent trend dimensions. This is designed to find sustained improving
/// lines early without declaring a play-rate spike alone to be a meta breakthrough.
/// </summary>
public static class EmergingMetaSignalBuilder
{
    public static EmergingMetaSignal Build(IReadOnlyList<MetricTrendEvidence> evidence)
    {
        var usable = evidence
            .Where(x => x.Importance > 0 && x.Signal.Confidence > 0)
            .ToArray();

        if (usable.Length < 2)
        {
            return new EmergingMetaSignal(
                EmergingMetaState.Unclear,
                0,
                usable.Length == 0 ? 0 : usable.Average(x => x.Signal.Confidence) * .5,
                usable,
                "At least two reliable metric trends are required.");
        }

        double signed = 0;
        double total = 0;
        foreach (var metric in usable)
        {
            var direction = (int)metric.Signal.Direction;
            if (!metric.HigherIsBetter) direction *= -1;
            var weight = Math.Clamp(metric.Importance, 0, 3) * metric.Signal.Confidence;
            signed += direction * metric.Signal.Strength * weight;
            total += weight;
        }

        var normalized = total <= 1e-9 ? 0 : Math.Clamp(signed / total, -1, 1);
        var confidence = Math.Clamp(
            usable.Average(x => x.Signal.Confidence) *
            (1.0 - Math.Exp(-usable.Count(x => x.Signal.Direction != TrendDirection.Flat) / 2.0)),
            0,
            1);

        // A strong signal requires performance evidence, not just popularity. Metric naming is
        // adapter-defined, so callers mark placement/top4/win metrics with higher importance.
        var state = normalized switch
        {
            >= .12 when confidence >= .45 => EmergingMetaState.Emerging,
            <= -.12 when confidence >= .45 => EmergingMetaState.Cooling,
            _ => EmergingMetaState.Unclear
        };

        return new EmergingMetaSignal(
            state,
            Math.Abs(normalized),
            confidence,
            usable,
            $"Composite signed trend {normalized:F3} across {usable.Length} metrics.");
    }
}
