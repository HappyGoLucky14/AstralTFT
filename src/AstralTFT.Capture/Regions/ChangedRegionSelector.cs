using AstralTFT.Capture.Abstractions;

namespace AstralTFT.Capture.Regions;

public enum RecognitionPriority
{
    Background = 0,
    Normal = 1,
    Important = 2,
    Immediate = 3
}

public sealed record RecognitionRegion(
    RegionOfInterest Region,
    RecognitionPriority Priority,
    TimeSpan MinimumRecheckInterval,
    double MinimumMeaningfulChange = 0.0);

public sealed record ChangedRegionWork(
    RegionOfInterest Region,
    RecognitionPriority Priority,
    long FrameSequence,
    DateTimeOffset CapturedAt,
    double ChangeScore);

/// <summary>
/// Keeps cheap ROI fingerprint work bounded and prioritizes transient UI such as augment/shop
/// changes over background verification. Recognition itself is performed by downstream workers.
/// </summary>
public sealed class ChangedRegionSelector
{
    private readonly IRegionChangeDetector _changeDetector;
    private readonly Dictionary<string, DateTimeOffset> _lastChecked = new(StringComparer.OrdinalIgnoreCase);

    public ChangedRegionSelector(IRegionChangeDetector changeDetector)
    {
        _changeDetector = changeDetector;
    }

    public IReadOnlyList<ChangedRegionWork> Select(
        CapturedFrame frame,
        IEnumerable<RecognitionRegion> regions,
        int maxRegions = int.MaxValue)
    {
        if (maxRegions <= 0) return Array.Empty<ChangedRegionWork>();

        var changed = new List<ChangedRegionWork>();
        foreach (var definition in regions)
        {
            if (_lastChecked.TryGetValue(definition.Region.Id, out var last) &&
                frame.CapturedAt - last < definition.MinimumRecheckInterval)
                continue;

            _lastChecked[definition.Region.Id] = frame.CapturedAt;
            var result = _changeDetector.Compare(frame, definition.Region);
            var threshold = Math.Max(0, definition.MinimumMeaningfulChange);
            if (!result.IsMeaningful || result.ChangeScore < threshold) continue;

            changed.Add(new ChangedRegionWork(
                definition.Region,
                definition.Priority,
                frame.Sequence,
                frame.CapturedAt,
                result.ChangeScore));
        }

        return changed
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.ChangeScore)
            .Take(maxRegions)
            .ToArray();
    }
}
