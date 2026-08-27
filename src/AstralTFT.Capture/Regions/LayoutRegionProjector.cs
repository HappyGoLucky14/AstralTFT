using AstralTFT.Capture.Abstractions;
using AstralTFT.Core.Models;

namespace AstralTFT.Capture.Regions;

public static class LayoutRegionProjector
{
    public static RegionOfInterest Project(LayoutRegion region, int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0) throw new ArgumentOutOfRangeException(nameof(frameWidth));
        if (frameHeight <= 0) throw new ArgumentOutOfRangeException(nameof(frameHeight));

        var bounds = region.Bounds.Clamp();
        var left = (int)Math.Floor(bounds.X * frameWidth);
        var top = (int)Math.Floor(bounds.Y * frameHeight);
        var right = (int)Math.Ceiling((bounds.X + bounds.Width) * frameWidth);
        var bottom = (int)Math.Ceiling((bounds.Y + bounds.Height) * frameHeight);

        left = Math.Clamp(left, 0, frameWidth - 1);
        top = Math.Clamp(top, 0, frameHeight - 1);
        right = Math.Clamp(right, left + 1, frameWidth);
        bottom = Math.Clamp(bottom, top + 1, frameHeight);

        return new RegionOfInterest(region.Id, left, top, right - left, bottom - top);
    }

    public static IReadOnlyList<RegionOfInterest> ProjectRecognitionRegions(
        LayoutProfile profile,
        int frameWidth,
        int frameHeight) => profile.Regions
            .Where(x => x.IsRecognitionRegion)
            .Select(x => Project(x, frameWidth, frameHeight))
            .ToArray();
}
