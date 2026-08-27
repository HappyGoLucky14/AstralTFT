using AstralTFT.Analysis.Coaching;

namespace AstralTFT.Analysis.Policy;

public enum MatchLifecycle
{
    OutsideMatch = 0,
    Loading = 1,
    InProgress = 2,
    Finished = 3
}

public enum AnalysisContentKind
{
    StaticReference = 0,
    RecognitionDiagnostics = 1,
    StateAwareRecommendation = 2,
    OpponentDerivedRecommendation = 3
}

/// <summary>
/// Product-policy boundary. Deep analysis may exist internally, but state-aware prescriptions are
/// not rendered during an active TFT match. This keeps policy changes isolated from capture/state
/// code and gives us one auditable enforcement point.
/// </summary>
public static class PresentationPolicy
{
    public static bool CanPresent(AnalysisContentKind kind, MatchLifecycle lifecycle, bool developerDiagnostics = false)
    {
        return kind switch
        {
            AnalysisContentKind.StaticReference => true,
            AnalysisContentKind.RecognitionDiagnostics => developerDiagnostics,
            AnalysisContentKind.StateAwareRecommendation => lifecycle == MatchLifecycle.Finished,
            AnalysisContentKind.OpponentDerivedRecommendation => false,
            _ => false
        };
    }

    public static bool CanPresent(RoundReview review, MatchLifecycle lifecycle) =>
        review.Availability != ReviewAvailability.Hidden &&
        CanPresent(AnalysisContentKind.StateAwareRecommendation, lifecycle);
}
