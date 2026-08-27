namespace AstralTFT.State.Tracking;

public enum ThreeStarPursuitState
{
    Closed = 0,
    LowProbability = 1,
    Open = 2,
    HighProbability = 3
}

public sealed record ThreeStarPursuitInput(
    int UnitCost,
    int OwnedBaseCopies,
    int? SharedPoolSize,
    int KnownUnavailableCopies,
    int Gold,
    int Level,
    string? Stage,
    bool HasChampionDuplicator = false);

public sealed record ThreeStarPursuitResult(
    ThreeStarPursuitState State,
    int CopiesNeeded,
    int? CopiesPotentiallyAvailable,
    string Reason);

public static class ThreeStarPursuitEvaluator
{
    public static ThreeStarPursuitResult Evaluate(ThreeStarPursuitInput input)
    {
        var needed = Math.Max(0, 9 - input.OwnedBaseCopies);
        if (needed == 0)
            return new(ThreeStarPursuitState.Closed, 0, input.SharedPoolSize, "3-star already complete.");

        // User preference: normal post-2-star pursuit behavior is special for 4/5-costs.
        if (input.UnitCost is not (4 or 5))
            return new(ThreeStarPursuitState.Closed, needed, null, "Automatic post-2-star pursuit is reserved for 4/5-cost units.");

        if (input.OwnedBaseCopies < 3)
            return new(ThreeStarPursuitState.Open, needed, null, "2-star target is not complete yet.");

        int? available = null;
        if (input.SharedPoolSize is int pool)
        {
            available = Math.Max(0, pool - input.KnownUnavailableCopies - input.OwnedBaseCopies);
            if (available + (input.HasChampionDuplicator ? 1 : 0) < needed)
            {
                return new(
                    ThreeStarPursuitState.Closed,
                    needed,
                    available,
                    "Not enough known copies remain to complete 3-star.");
            }
        }

        // Conservative bootstrap heuristic. The future roll-EV model replaces this with actual
        // level/shop odds, stage tempo and gold-to-hit distributions.
        var momentum = input.OwnedBaseCopies switch
        {
            >= 7 => 3,
            >= 6 => 2,
            >= 5 => 1,
            _ => 0
        };

        if (input.Gold >= 50) momentum++;
        if (input.UnitCost == 5 && input.Level < 9) momentum--;
        if (input.UnitCost == 4 && input.Level < 8) momentum--;

        // Known copies held elsewhere reduce practical hit probability even when the chase is
        // still mathematically possible. These are contextual penalties, not hard-coded pool sizes.
        if (input.UnitCost == 4 && input.KnownUnavailableCopies >= 2) momentum--;
        if (input.UnitCost == 5 && input.KnownUnavailableCopies >= 1) momentum--;

        return momentum switch
        {
            >= 3 => new(ThreeStarPursuitState.HighProbability, needed, available,
                "Enough owned copies/economy to keep the 3-star line highly relevant."),
            >= 1 => new(ThreeStarPursuitState.Open, needed, available,
                "3-star remains a viable optional win condition."),
            _ => new(ThreeStarPursuitState.LowProbability, needed, available,
                "Mathematically possible, but current copies/economy make the chase speculative.")
        };
    }
}
