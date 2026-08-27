namespace AstralTFT.Capture.Abstractions;

public readonly record struct RegionOfInterest(string Id, int X, int Y, int Width, int Height);

public sealed record RegionChange(string RegionId, double ChangeScore, bool IsMeaningful);

public interface IRegionChangeDetector
{
    RegionChange Compare(CapturedFrame frame, RegionOfInterest region);
}
