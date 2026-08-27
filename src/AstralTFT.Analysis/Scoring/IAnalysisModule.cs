using AstralTFT.Core.Models;

namespace AstralTFT.Analysis.Scoring;

public interface IAnalysisModule
{
    string Id { get; }
    ValueTask<IReadOnlyList<ScoreBreakdown>> AnalyzeAsync(
        GameState state,
        CancellationToken cancellationToken = default);
}
