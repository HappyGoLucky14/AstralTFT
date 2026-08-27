namespace AstralTFT.Meta.Ensembling;

public sealed record MetricObservation(
    string SourceId,
    double Value,
    double SourceQuality,
    long? SampleSize = null,
    DateTimeOffset? CapturedAt = null);

public sealed record MetricContribution(
    string SourceId,
    double Value,
    double EffectiveWeight,
    double OutlierFactor);

public sealed record MetricEstimate(
    double Value,
    double Confidence,
    IReadOnlyList<MetricContribution> Contributions,
    string Reason);

/// <summary>
/// Blends equivalent metrics from multiple providers while reducing the authority of a single
/// disagreeing source. This is intentionally metric-agnostic: placement, top-four rate, pick rate,
/// etc. must be normalized before reaching this layer.
/// </summary>
public static class RobustMetricEnsembler
{
    public static MetricEstimate Estimate(IReadOnlyList<MetricObservation> observations)
    {
        var valid = observations
            .Where(x => double.IsFinite(x.Value) && x.SourceQuality > 0)
            .ToArray();

        if (valid.Length == 0)
            return new MetricEstimate(0, 0, Array.Empty<MetricContribution>(), "No usable source observations.");

        var baseWeights = valid.Select(BaseWeight).ToArray();
        var median = WeightedMedian(valid.Select((x, i) => (x.Value, baseWeights[i])).ToArray());
        var deviations = valid.Select((x, i) => (Math.Abs(x.Value - median), baseWeights[i])).ToArray();
        var mad = WeightedMedian(deviations);

        var contributions = new List<MetricContribution>(valid.Length);
        double weightedValue = 0;
        double totalWeight = 0;

        for (var i = 0; i < valid.Length; i++)
        {
            var obs = valid[i];
            var baseWeight = baseWeights[i];
            var outlierFactor = OutlierFactor(obs.Value, median, mad);
            var effective = baseWeight * outlierFactor;
            contributions.Add(new MetricContribution(obs.SourceId, obs.Value, effective, outlierFactor));
            weightedValue += obs.Value * effective;
            totalWeight += effective;
        }

        var estimate = totalWeight <= 1e-9 ? median : weightedValue / totalWeight;
        var sourceDiversity = 1.0 - Math.Exp(-valid.Select(x => x.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() / 2.0);
        var quality = WeightedAverage(valid.Select((x, i) => (x.SourceQuality, baseWeights[i])).ToArray());
        var sampleConfidence = 1.0 - Math.Exp(-valid.Sum(x => Math.Max(0L, x.SampleSize ?? 0L)) / 20_000.0);
        if (valid.All(x => x.SampleSize is null)) sampleConfidence = 0.55;

        var agreement = Agreement(valid.Select(x => x.Value).ToArray(), estimate);
        var confidence = Math.Clamp(
            (quality * 0.40) + (sampleConfidence * 0.25) + (sourceDiversity * 0.15) + (agreement * 0.20),
            0,
            1);

        return new MetricEstimate(
            estimate,
            confidence,
            contributions.OrderByDescending(x => x.EffectiveWeight).ToArray(),
            $"Robust blend of {valid.Length} observation(s); weighted median {median:F4}, agreement {agreement:P0}.");
    }

    private static double BaseWeight(MetricObservation observation)
    {
        var quality = Math.Clamp(observation.SourceQuality, 0, 1);
        var sample = observation.SampleSize switch
        {
            null => 0.65,
            <= 0 => 0.15,
            _ => 1.0 - Math.Exp(-observation.SampleSize.Value / 5_000.0)
        };

        return Math.Max(1e-6, quality * sample);
    }

    private static double OutlierFactor(double value, double median, double mad)
    {
        if (mad <= 1e-9) return Math.Abs(value - median) <= 1e-9 ? 1.0 : 0.35;

        // 1.4826 makes MAD comparable to standard deviation under a normal distribution.
        var robustZ = Math.Abs(value - median) / (1.4826 * mad);
        if (robustZ <= 2.0) return 1.0;
        if (robustZ >= 6.0) return 0.10;
        return Math.Exp(-0.5 * Math.Pow((robustZ - 2.0) / 1.5, 2));
    }

    private static double Agreement(IReadOnlyList<double> values, double center)
    {
        if (values.Count <= 1) return 0.60;
        var scale = Math.Max(0.01, Math.Abs(center));
        var meanRelativeError = values.Average(x => Math.Abs(x - center) / scale);
        return Math.Clamp(Math.Exp(-meanRelativeError * 4.0), 0, 1);
    }

    private static double WeightedMedian(IReadOnlyList<(double Value, double Weight)> values)
    {
        var ordered = values.OrderBy(x => x.Value).ToArray();
        var total = ordered.Sum(x => Math.Max(0, x.Weight));
        if (total <= 1e-9) return ordered[ordered.Length / 2].Value;

        var target = total / 2.0;
        double running = 0;
        foreach (var item in ordered)
        {
            running += Math.Max(0, item.Weight);
            if (running >= target) return item.Value;
        }

        return ordered[^1].Value;
    }

    private static double WeightedAverage(IReadOnlyList<(double Value, double Weight)> values)
    {
        var totalWeight = values.Sum(x => Math.Max(0, x.Weight));
        if (totalWeight <= 1e-9) return 0;
        return values.Sum(x => x.Value * Math.Max(0, x.Weight)) / totalWeight;
    }
}
