namespace AstralTFT.Capture.Recognition;

/// <summary>
/// Prevents a slow older detector result from overwriting a newer accepted result
/// for the same detector/region pair when recognition workers finish out of order.
/// </summary>
public sealed class RecognitionResultSequenceGate
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _latestAccepted = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAccept(RecognitionBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var key = batch.DetectorId + "\u001f" + batch.RegionId;

        lock (_gate)
        {
            if (_latestAccepted.TryGetValue(key, out var latest) && batch.FrameSequence <= latest)
                return false;

            _latestAccepted[key] = batch.FrameSequence;
            return true;
        }
    }

    public long? LatestSequence(string detectorId, string regionId)
    {
        var key = detectorId + "\u001f" + regionId;
        lock (_gate)
            return _latestAccepted.TryGetValue(key, out var value) ? value : null;
    }

    public void Reset()
    {
        lock (_gate) _latestAccepted.Clear();
    }
}
