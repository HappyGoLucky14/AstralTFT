namespace AstralTFT.Meta.Patches;

public sealed record PatchBlendInput(
    TimeSpan TimeSincePatchLaunch,
    long NewPatchSampleSize,
    double NewPatchSourceQuality,
    bool IsMajorSetLaunch = false);

public sealed record PatchBlendWeights(
    double PreviousPatchWeight,
    double CurrentPatchWeight,
    string Reason);

/// <summary>
/// Bootstraps a new patch from previous-patch priors, then rapidly hands authority to fresh data.
/// The constants are deliberately centralized so they can be tuned from observed backtests.
/// </summary>
public sealed record PatchBlendPolicy(
    double PriorHalfLifeHours = 3.0,
    long ReliableSampleTarget = 25_000,
    double MajorSetPriorMultiplier = 0.45)
{
    public PatchBlendWeights Calculate(PatchBlendInput input)
    {
        var hours = Math.Max(0, input.TimeSincePatchLaunch.TotalHours);
        var timePrior = Math.Pow(0.5, hours / Math.Max(0.25, PriorHalfLifeHours));
        var sampleMaturity = 1.0 - Math.Exp(-Math.Max(0, input.NewPatchSampleSize) / (double)Math.Max(1, ReliableSampleTarget));
        var quality = Math.Clamp(input.NewPatchSourceQuality, 0, 1);

        var previous = timePrior * (1.0 - (0.85 * sampleMaturity * quality));
        if (input.IsMajorSetLaunch) previous *= MajorSetPriorMultiplier;

        previous = Math.Clamp(previous, 0.02, 0.95);
        var current = 1.0 - previous;

        return new PatchBlendWeights(
            previous,
            current,
            $"Fresh patch weight rises with time, sample maturity ({sampleMaturity:P0}) and source quality ({quality:P0}).");
    }
}
