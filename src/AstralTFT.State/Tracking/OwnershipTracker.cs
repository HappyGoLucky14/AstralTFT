using AstralTFT.Core.Models;

namespace AstralTFT.State.Tracking;

public static class OwnershipTracker
{
    public static IReadOnlyDictionary<string, int> CountBaseCopies(
        IEnumerable<UnitInstance> board,
        IEnumerable<UnitInstance> bench)
    {
        return board.Concat(bench)
            .Where(x => !string.IsNullOrWhiteSpace(x.ChampionId))
            .GroupBy(x => x.ChampionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.BaseCopyEquivalent),
                StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsTwoStarComplete(int baseCopies) => baseCopies >= 3;
    public static bool IsThreeStarComplete(int baseCopies) => baseCopies >= 9;
}
