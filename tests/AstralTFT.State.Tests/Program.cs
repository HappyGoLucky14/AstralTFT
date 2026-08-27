using AstralTFT.Core.Models;
using AstralTFT.State.Events;
using AstralTFT.State.Inference;
using AstralTFT.State.Reducers;
using AstralTFT.State.Timeline;
using AstralTFT.State.Tracking;

var tests = new (string Name, Action Run)[]
{
    ("Stage parsing", StageParsing),
    ("Ownership base-copy counting", OwnershipCounting),
    ("Roster reduction updates ownership", RosterReduction),
    ("Timeline replay is deterministic", TimelineReplay),
    ("PvE acquisition inference subtracts purchases", PveInference),
    ("4-cost 3-star closes when pool is exhausted", FourCostPoolClosure),
    ("5-cost unavailable copies reduce pursuit", FiveCostAvailabilityPenalty)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} self-test(s) failed.");
    return 1;
}

Console.WriteLine($"All {tests.Length} state self-tests passed.");
return 0;

static void StageParsing()
{
    True(StagePoint.TryParse("3-5", out var point), "3-5 should parse.");
    Equal(3, point.Stage);
    Equal(5, point.Round);
    True(!StagePoint.TryParse("bad", out _), "Invalid stage must not parse.");
}

static void OwnershipCounting()
{
    var now = DateTimeOffset.UtcNow;
    var board = new[] { Unit("nidalee", 2, UnitLocation.Board, 0, now) };
    var bench = new[] { Unit("nidalee", 1, UnitLocation.Bench, 0, now) };
    var copies = OwnershipTracker.CountBaseCopies(board, bench);
    Equal(4, copies["nidalee"]);
}

static void RosterReduction()
{
    var now = DateTimeOffset.UtcNow;
    var state = GameState.Empty(now);
    var board = new[] { Unit("yi", 2, UnitLocation.Board, 0, now) };
    var bench = new[] { Unit("yi", 1, UnitLocation.Bench, 0, now) };
    var evt = new RosterSnapshotAcceptedEvent(state.GameId, now.AddSeconds(1), board, bench, new Confidence(.99));
    var reduced = new GameStateReducer().Apply(state, evt);
    Equal(4, reduced.OwnedBaseCopies["yi"]);
    Equal(1, reduced.Board.Count);
}

static void TimelineReplay()
{
    var now = DateTimeOffset.UtcNow;
    var initial = GameState.Empty(now);
    var timeline = new GameTimeline(initial);
    timeline.Append(new GoldChangedEvent(initial.GameId, now.AddSeconds(1), null, 10));
    timeline.Append(new LevelChangedEvent(initial.GameId, now.AddSeconds(2), null, 4));
    timeline.Append(new StageChangedEvent(initial.GameId, now.AddSeconds(3), null, "2-1"));

    var replayed = GameTimeline.Replay(initial, timeline.Events);
    Equal<int?>(10, replayed.Player.Gold);
    Equal<int?>(4, replayed.Player.Level);
    Equal("2-1", replayed.Stage);
}

static void PveInference()
{
    var now = DateTimeOffset.UtcNow;
    var before = WithRoster(GameState.Empty(now), [], []);
    var after = WithRoster(before with { UpdatedAt = now.AddSeconds(1) },
        [Unit("three_cost", 1, UnitLocation.Bench, 0, now.AddSeconds(1))], []);

    var inferred = AcquisitionInferenceEngine.Infer(new AcquisitionInferenceInput(
        before,
        after,
        new Dictionary<string, int>(),
        IsPveOrLootContext: true,
        ObservationGap: TimeSpan.FromSeconds(1)));

    Equal(1, inferred.Count);
    Equal(AcquisitionSource.PveOrLoot, inferred[0].Source);
    Equal(1, inferred[0].BaseCopies);
}

static void FourCostPoolClosure()
{
    var result = ThreeStarPursuitEvaluator.Evaluate(new ThreeStarPursuitInput(
        UnitCost: 4,
        OwnedBaseCopies: 6,
        SharedPoolSize: 10,
        KnownUnavailableCopies: 2,
        Gold: 60,
        Level: 8,
        Stage: "5-1"));

    // 10 total - 6 ours - 2 unavailable = 2 obtainable, but 3 are needed.
    Equal(ThreeStarPursuitState.Closed, result.State);
}

static void FiveCostAvailabilityPenalty()
{
    var noContest = ThreeStarPursuitEvaluator.Evaluate(new ThreeStarPursuitInput(
        UnitCost: 5, OwnedBaseCopies: 6, SharedPoolSize: 12, KnownUnavailableCopies: 0,
        Gold: 60, Level: 9, Stage: "5-2"));
    var contested = ThreeStarPursuitEvaluator.Evaluate(new ThreeStarPursuitInput(
        UnitCost: 5, OwnedBaseCopies: 6, SharedPoolSize: 12, KnownUnavailableCopies: 1,
        Gold: 60, Level: 9, Stage: "5-2"));

    True((int)contested.State <= (int)noContest.State, "Known unavailable 5-cost copy must not improve pursuit state.");
}

static UnitInstance Unit(string id, int star, UnitLocation location, int slot, DateTimeOffset at) => new(
    Guid.NewGuid(), id, star, location, slot, Array.Empty<string>(), new Confidence(.99), at, AcquisitionSource.Unknown);

static GameState WithRoster(GameState state, IReadOnlyList<UnitInstance> bench, IReadOnlyList<UnitInstance> board)
{
    var copies = OwnershipTracker.CountBaseCopies(board, bench);
    return state with { Board = board, Bench = bench, OwnedBaseCopies = copies };
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
