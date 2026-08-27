namespace AstralTFT.Core.Models;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public NormalizedRect Clamp() => new(
        Math.Clamp(X, 0, 1),
        Math.Clamp(Y, 0, 1),
        Math.Clamp(Width, 0, 1),
        Math.Clamp(Height, 0, 1));
}

public sealed record LayoutRegion(
    string Id,
    NormalizedRect Bounds,
    bool IsSafeOverlayZone = false,
    bool IsRecognitionRegion = true);

public sealed record LayoutProfile(
    string Id,
    string ClientFamily,
    string? MinPatch,
    string? MaxPatch,
    int ReferenceWidth,
    int ReferenceHeight,
    IReadOnlyList<LayoutRegion> Regions,
    bool IsProvisional = true);
