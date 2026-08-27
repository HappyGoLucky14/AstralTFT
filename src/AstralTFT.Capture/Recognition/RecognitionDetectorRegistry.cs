namespace AstralTFT.Capture.Recognition;

/// <summary>
/// Maps each logical layout region to exactly one composite detector. Region-level
/// composites avoid copying the same ROI for several tiny recognisers; e.g. a shop
/// detector can classify champion slots and Wisps in one pass.
/// </summary>
public sealed class RecognitionDetectorRegistry
{
    private readonly Dictionary<string, IRegionObservationDetector> _byRegion;

    public RecognitionDetectorRegistry(IEnumerable<IRegionObservationDetector> detectors)
    {
        ArgumentNullException.ThrowIfNull(detectors);
        _byRegion = new Dictionary<string, IRegionObservationDetector>(StringComparer.OrdinalIgnoreCase);

        foreach (var detector in detectors)
        {
            ArgumentNullException.ThrowIfNull(detector);
            var descriptor = detector.Descriptor;
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);

            foreach (var regionId in descriptor.SupportedRegionIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
                if (!_byRegion.TryAdd(regionId, detector))
                {
                    var existing = _byRegion[regionId];
                    throw new InvalidOperationException(
                        $"Recognition region '{regionId}' is claimed by both " +
                        $"'{existing.Descriptor.Id}' and '{descriptor.Id}'. Use one composite detector per region.");
                }
            }
        }
    }

    public bool TryResolve(string regionId, out IRegionObservationDetector detector) =>
        _byRegion.TryGetValue(regionId, out detector!);

    public IReadOnlyCollection<string> RegisteredRegionIds => _byRegion.Keys.ToArray();
}
