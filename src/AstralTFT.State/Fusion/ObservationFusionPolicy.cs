using AstralTFT.Core.Models;

namespace AstralTFT.State.Fusion;

public sealed record ObservationFusionPolicy(
    double ProbableThreshold = 0.85,
    double ConfirmedThreshold = 0.97,
    TimeSpan ProbableConfirmationWindow = default)
{
    public TimeSpan ConfirmationWindow => ProbableConfirmationWindow == default
        ? TimeSpan.FromMilliseconds(750)
        : ProbableConfirmationWindow;

    public ConfidenceState Classify(Confidence confidence) =>
        confidence.Classify(ProbableThreshold, ConfirmedThreshold);
}
