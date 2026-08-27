using AstralTFT.Core.Models;

namespace AstralTFT.Capture.Regions;

public sealed record LayoutSelection(
    LayoutProfile? Profile,
    double Confidence,
    bool RequiresCalibration,
    string Reason);

public sealed class LayoutProfileRegistry
{
    private readonly IReadOnlyList<LayoutProfile> _profiles;

    public LayoutProfileRegistry(IEnumerable<LayoutProfile> profiles)
    {
        _profiles = profiles.ToArray();
    }

    public LayoutSelection Select(
        string clientFamily,
        string? patch,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            return new LayoutSelection(null, 0, true, "Invalid capture dimensions.");

        var candidates = _profiles
            .Where(x => string.Equals(x.ClientFamily, clientFamily, StringComparison.OrdinalIgnoreCase))
            .Select(x => (profile: x, score: Score(x, patch, width, height)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ToArray();

        if (candidates.Length == 0)
            return new LayoutSelection(null, 0, true, "No profile matches the detected client family.");

        var best = candidates[0];
        var confidence = Math.Clamp(best.score, 0, 1);
        var needsCalibration = best.profile.IsProvisional || confidence < .80;
        return new LayoutSelection(
            best.profile,
            confidence,
            needsCalibration,
            needsCalibration
                ? "Best layout match is provisional or below the calibration confidence threshold."
                : "Compatible client/patch/aspect layout profile selected.");
    }

    private static double Score(LayoutProfile profile, string? patch, int width, int height)
    {
        var patchScore = PatchCompatibility(profile, patch);
        if (patchScore <= 0) return 0;

        var currentAspect = width / (double)height;
        var referenceAspect = profile.ReferenceWidth / (double)profile.ReferenceHeight;
        var aspectError = Math.Abs(currentAspect - referenceAspect) / referenceAspect;
        var aspectScore = Math.Exp(-aspectError * 10.0);

        var resolutionScale = Math.Min(
            width / (double)Math.Max(1, profile.ReferenceWidth),
            height / (double)Math.Max(1, profile.ReferenceHeight));
        var resolutionScore = Math.Clamp(resolutionScale, .55, 1.0);

        var provisionalPenalty = profile.IsProvisional ? .88 : 1.0;
        return Math.Clamp((patchScore * .50 + aspectScore * .35 + resolutionScore * .15) * provisionalPenalty, 0, 1);
    }

    private static double PatchCompatibility(LayoutProfile profile, string? patch)
    {
        if (!TftPatchVersion.TryParse(patch, out var current)) return .72;

        if (TftPatchVersion.TryParse(profile.MinPatch, out var min) && current.CompareTo(min) < 0)
            return 0;
        if (TftPatchVersion.TryParse(profile.MaxPatch, out var max) && current.CompareTo(max) > 0)
            return 0;

        return 1.0;
    }
}
