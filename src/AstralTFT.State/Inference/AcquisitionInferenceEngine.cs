using AstralTFT.Core.Models;

namespace AstralTFT.State.Inference;

public sealed record AcquisitionInferenceInput(
    GameState Previous,
    GameState Current,
    IReadOnlyDictionary<string, int> ConfirmedPurchasedBaseCopies,
    bool IsPveOrLootContext,
    bool IsCarouselContext = false,
    TimeSpan? ObservationGap = null);

public sealed record InferredAcquisition(
    string ChampionId,
    int BaseCopies,
    AcquisitionSource Source,
    Confidence Confidence,
    string Reason);

public static class AcquisitionInferenceEngine
{
    public static IReadOnlyList<InferredAcquisition> Infer(AcquisitionInferenceInput input)
    {
        var championIds = input.Previous.OwnedBaseCopies.Keys
            .Concat(input.Current.OwnedBaseCopies.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var results = new List<InferredAcquisition>();

        foreach (var championId in championIds)
        {
            input.Previous.OwnedBaseCopies.TryGetValue(championId, out var before);
            input.Current.OwnedBaseCopies.TryGetValue(championId, out var after);
            var positiveDelta = Math.Max(0, after - before);
            if (positiveDelta == 0) continue;

            input.ConfirmedPurchasedBaseCopies.TryGetValue(championId, out var purchased);
            var unexplained = Math.Max(0, positiveDelta - Math.Max(0, purchased));
            if (unexplained == 0) continue;

            var source = input.IsCarouselContext
                ? AcquisitionSource.Carousel
                : input.IsPveOrLootContext
                    ? AcquisitionSource.PveOrLoot
                    : AcquisitionSource.Unknown;

            var confidenceValue = source switch
            {
                AcquisitionSource.PveOrLoot => 0.96,
                AcquisitionSource.Carousel => 0.96,
                _ => 0.72
            };

            // Long observation gaps make attribution less reliable because an unseen purchase/sale
            // could have happened between accepted snapshots.
            if (input.ObservationGap is { } gap && gap > TimeSpan.FromSeconds(3))
                confidenceValue -= 0.12;

            results.Add(new InferredAcquisition(
                championId,
                unexplained,
                source,
                new Confidence(Math.Clamp(confidenceValue, 0, 1)),
                source switch
                {
                    AcquisitionSource.PveOrLoot => "Owned-copy count increased without a matching purchase during a PvE/loot context.",
                    AcquisitionSource.Carousel => "Owned-copy count increased without a matching purchase during carousel context.",
                    _ => "Owned-copy count increased without enough matching purchase evidence."
                }));
        }

        return results;
    }
}
