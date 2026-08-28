using AstralTFT.Capture.Abstractions;
using AstralTFT.Capture.Regions;
using AstralTFT.Capture.Recognition;
using AstralTFT.Capture.Replay;
using AstralTFT.Capture.Windows;
using AstralTFT.Core.Models;
using AstralTFT.Meta.Ensembling;
using AstralTFT.Infrastructure.Diagnostics;
using AstralTFT.Infrastructure.Networking;
using AstralTFT.Analysis.Coaching;
using AstralTFT.Analysis.Personalization;
using AstralTFT.Analysis.Playstyles;
using AstralTFT.Analysis.Policy;
using System.Net;
using System.Net.Http.Headers;
using AstralTFT.Meta.Patches;
using AstralTFT.Meta.Trends;
using AstralTFT.Meta.Ranks;
using AstralTFT.State.Fusion;
using AstralTFT.State.Actors;
using AstralTFT.State.Events;

var tests = new (string Name, Action Run)[]
{
    ("Normalized layout projects to pixels", LayoutProjection),
    ("Luma detector ignores identical frames", LumaNoChange),
    ("Luma detector notices changed ROI", LumaChange),
    ("Luma detector suppresses tiny jitter", LumaTinyJitter),
    ("Region selector honors priority", RegionPriority),
    ("Probable observation requires confirmation", FusionNeedsConfirmation),
    ("Low confidence cannot erase stable state", FusionRetainsStable),
    ("Robust meta blend resists outlier", RobustBlend),
    ("Patch blend hands authority to fresh data", PatchBlendMatures),
    ("Trend detector identifies rising series", RisingTrend),
    ("Performance governor backs off under pressure", GovernorBackoff),
    ("Performance governor sleeps when TFT minimized", GovernorSleep),
    ("State actor serializes events", StateActorSerializes),
    ("Conditional HTTP cache reuses 304 payload", ConditionalCache304),
    ("Rank blend keeps player context and high-Elo signal", RankBlend),
    ("Emerging meta signal rewards multi-metric improvement", EmergingSignal),
    ("Generic round review is suppressed", GenericReviewSuppressed),
    ("Specific comp-aware review clears gate", SpecificReviewAccepted),
    ("Personalization weight grows conservatively", PersonalizationMatures),
    ("Comp resolver avoids premature line lock", CompResolverAmbiguity),
    ("Live state-aware coaching is presentation-gated", LiveRecommendationGated),
    ("Patch version parses suffix safely", PatchVersionParsing),
    ("Layout registry prefers compatible profile", LayoutRegistrySelects),
    ("CPU ROI snapshot copies only selected pixels", CpuSnapshotCopiesRoi),
    ("Recognition queue coalesces same region", RecognitionQueueCoalesces),
    ("Recognition queue protects higher priority work", RecognitionQueueProtectsPriority),
    ("Detector circuit breaker isolates failures", DetectorCircuitBreaker),
    ("Recognition dispatcher skips stale work", RecognitionDispatcherSkipsStale),
    ("Recognition result gate rejects out-of-order batches", RecognitionResultGateRejectsOld),
    ("Detector registry rejects ambiguous region ownership", DetectorRegistryRejectsDuplicates),
    ("Frame pump disposes capture lease after ROI routing", FramePumpDisposesCaptureLease),
    ("TFT window selector rejects guide/browser false positives", TftWindowSelectorRejectsFalsePositives),
    ("Normalized WGC ROI projects deterministically", NormalizedWgcRoiProjectsDeterministically),
    ("Shop structure recognizer finds empty and cost tiers", ShopStructureRecognition),
    ("Shop cost bar median resists glyph noise", ShopCostBarMedianResistsGlyphNoise),
    ("Shop HUD gate rejects incomplete false positives", ShopHudGateRejectsIncompleteFalsePositives),
    ("Shop HUD requires repeated frame anchors", ShopHudRequiresFrameAnchors),
    ("Shop HUD accepts greyed unaffordable chrome", ShopHudAcceptsGreyedChrome),
    ("Shop HUD accepts low-luma greyed chrome", ShopHudAcceptsLowLumaGreyedChrome),
    ("Shop HUD rejects uniform neutral scenery", ShopHudRejectsUniformNeutralScenery),
    ("Shop HUD rejects uniform tinted scenery", ShopHudRejectsUniformTintedScenery),
    ("Shop HUD rejects weak frame coverage", ShopHudRejectsWeakFrameCoverage),
    ("Shop HUD accepts calibrated frame coverage", ShopHudAcceptsCalibratedFrameCoverage),
    ("Corpus hash is deterministic and geometry-sensitive", CorpusHashIsDeterministic),
    ("Corpus contracts reject unsafe geometry", CorpusContractsRejectUnsafeGeometry),
    ("Corpus store replays exact snapshots in order", CorpusRoundTripsInOrder),
    ("Corpus store deduplicates blobs", CorpusStoreDeduplicatesBlobs),
    ("Corpus reader rejects hash mismatch", CorpusReaderRejectsHashMismatch),
    ("Corpus reader ignores only incomplete final line", CorpusReaderIgnoresIncompleteTail),
    ("Corpus reader rejects complete final JSON corruption", CorpusReaderRejectsCompleteFinalJsonCorruption),
    ("Corpus reader rejects invalid final metadata", CorpusReaderRejectsInvalidFinalMetadata),
    ("Corpus reader normalizes a missing blob directory", CorpusReaderNormalizesMissingBlobDirectory),
    ("Corpus reader rejects newline-terminated trailing JSON comma", CorpusReaderRejectsTerminatedTrailingJsonComma),
    ("Corpus reader rejects newline-terminated unterminated JSON string", CorpusReaderRejectsTerminatedUnterminatedJsonString),
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
    Console.Error.WriteLine($"{failures.Count} foundation self-test(s) failed.");
    return 1;
}

Console.WriteLine($"All {tests.Length} foundation self-tests passed.");
return 0;

static void LayoutProjection()
{
    var region = new LayoutRegion("shop", new NormalizedRect(.25, .80, .50, .15));
    var projected = LayoutRegionProjector.Project(region, 1920, 1080);
    Equal(480, projected.X);
    Equal(864, projected.Y);
    Equal(960, projected.Width);
    Equal(162, projected.Height);
}

static void LumaNoChange()
{
    var detector = new GridLumaRegionChangeDetector(4, 4, .02);
    var roi = new RegionOfInterest("shop", 0, 0, 8, 8);
    var first = Frame(1, 8, 8, 30);
    var second = Frame(2, 8, 8, 30);
    True(detector.Compare(first, roi).IsMeaningful, "Initial region should schedule recognition.");
    True(!detector.Compare(second, roi).IsMeaningful, "Identical frame should not schedule recognition.");
}

static void LumaChange()
{
    var detector = new GridLumaRegionChangeDetector(4, 4, .02);
    var roi = new RegionOfInterest("shop", 0, 0, 8, 8);
    _ = detector.Compare(Frame(1, 8, 8, 20), roi);
    var changed = detector.Compare(Frame(2, 8, 8, 220), roi);
    True(changed.IsMeaningful, "Large luminance change must be detected.");
    True(changed.ChangeScore > .5, "Expected a strong change score.");
}


static void LumaTinyJitter()
{
    var detector = new GridLumaRegionChangeDetector(4, 4, .025);
    var roi = new RegionOfInterest("shop", 0, 0, 8, 8);
    _ = detector.Compare(Frame(1, 8, 8, 80), roi);
    var changed = detector.Compare(Frame(2, 8, 8, 82), roi);

    True(!changed.IsMeaningful, "Two luminance levels of jitter should stay below the shop-change threshold.");
    True(changed.ChangeScore < .025, "Tiny jitter score should remain below the configured threshold.");
}

static void RegionPriority()
{
    var detector = new GridLumaRegionChangeDetector(2, 2, .01);
    var selector = new ChangedRegionSelector(detector);
    var now = DateTimeOffset.UtcNow;
    var regions = new[]
    {
        new RecognitionRegion(new RegionOfInterest("board", 0, 0, 4, 4), RecognitionPriority.Normal, TimeSpan.Zero),
        new RecognitionRegion(new RegionOfInterest("augment", 4, 0, 4, 4), RecognitionPriority.Immediate, TimeSpan.Zero)
    };

    var selected = selector.Select(Frame(1, 8, 4, 100, now), regions, maxRegions: 1);
    Equal(1, selected.Count);
    Equal("augment", selected[0].Region.Id);
}

static void FusionNeedsConfirmation()
{
    var now = DateTimeOffset.UtcNow;
    var fusion = new TemporalObservationFusion<int>();

    var initial = fusion.Observe(new Observation<int>(10, new Confidence(.99), "ocr", now));
    Equal(FusionDecisionKind.Accepted, initial.Kind);

    var first = fusion.Observe(new Observation<int>(11, new Confidence(.90), "ocr", now.AddMilliseconds(100)));
    Equal(FusionDecisionKind.Deferred, first.Kind);
    Equal(10, fusion.Accepted!.Value);

    var second = fusion.Observe(new Observation<int>(11, new Confidence(.91), "ocr", now.AddMilliseconds(250)));
    Equal(FusionDecisionKind.Accepted, second.Kind);
    Equal(11, fusion.Accepted!.Value);
}

static void FusionRetainsStable()
{
    var now = DateTimeOffset.UtcNow;
    var fusion = new TemporalObservationFusion<string>();
    _ = fusion.Observe(new Observation<string>("nidalee", new Confidence(.99), "shop", now));
    var weak = fusion.Observe(new Observation<string>("unknown", new Confidence(.50), "shop", now.AddMilliseconds(100)));
    Equal(FusionDecisionKind.Deferred, weak.Kind);
    Equal("nidalee", fusion.Accepted!.Value);
}

static void RobustBlend()
{
    var estimate = RobustMetricEnsembler.Estimate(new[]
    {
        new MetricObservation("a", 4.10, .95, 50_000),
        new MetricObservation("b", 4.14, .90, 30_000),
        new MetricObservation("c", 7.20, .60, 500)
    });

    True(estimate.Value < 4.5, "Low-quality/small-sample outlier should not dominate estimate.");
    True(estimate.Confidence > .50, "Two strong agreeing sources should produce usable confidence.");
}

static void PatchBlendMatures()
{
    var policy = new PatchBlendPolicy();
    var early = policy.Calculate(new PatchBlendInput(TimeSpan.FromMinutes(20), 600, .9));
    var mature = policy.Calculate(new PatchBlendInput(TimeSpan.FromHours(6), 80_000, .95));
    True(mature.CurrentPatchWeight > early.CurrentPatchWeight, "Fresh data should gain authority as sample/time mature.");
    True(mature.CurrentPatchWeight > .8, "Six-hour high-quality sample should strongly favor current patch.");
}

static void RisingTrend()
{
    var now = DateTimeOffset.UtcNow;
    var signal = TrendDetector.Detect(new[]
    {
        new TrendPoint(now.AddHours(-8), .08, 6_000, .9),
        new TrendPoint(now.AddHours(-6), .09, 7_000, .9),
        new TrendPoint(now.AddHours(-4), .12, 8_000, .92),
        new TrendPoint(now.AddHours(-2), .16, 9_000, .93),
        new TrendPoint(now.AddMinutes(-20), .19, 10_000, .94)
    }, now);

    Equal(TrendDirection.Rising, signal.Direction);
    True(signal.Confidence > .5, "Rising trend should have meaningful confidence at this sample size.");
}


static void GovernorBackoff()
{
    var governor = new AdaptivePerformanceGovernor();
    for (var i = 0; i < 3; i++)
    {
        governor.Observe(new PerformanceSample(
            DateTimeOffset.UtcNow.AddSeconds(i),
            ProcessCpuPercent: 8,
            WorkingSetBytes: 300_000_000,
            ProcessGpuPercent: 2,
            RecognitionQueueDepth: 4,
            P95RecognitionLatency: TimeSpan.FromMilliseconds(180)));
    }

    Equal(1, governor.CurrentBudget.MaxConcurrentDetectors);
    True(governor.CurrentBudget.MinRegionRecheckInterval >= TimeSpan.FromMilliseconds(150),
        "High pressure should reduce detector cadence.");
}

static void GovernorSleep()
{
    var governor = new AdaptivePerformanceGovernor();
    governor.Observe(new PerformanceSample(
        DateTimeOffset.UtcNow, 0, 200_000_000, 0, 0, TimeSpan.Zero,
        TftForeground: false, TftMinimized: true));
    Equal(0, governor.CurrentBudget.MaxConcurrentDetectors);
}


static void StateActorSerializes()
{
    var now = DateTimeOffset.UtcNow;
    var initial = GameState.Empty(now);
    var actor = new GameStateActor(initial);
    try
    {
        actor.PublishAsync(new GoldChangedEvent(initial.GameId, now.AddMilliseconds(1), null, 10)).AsTask().GetAwaiter().GetResult();
        actor.PublishAsync(new LevelChangedEvent(initial.GameId, now.AddMilliseconds(2), null, 4)).AsTask().GetAwaiter().GetResult();

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (actor.GetEventsSnapshot().Count < 2 && DateTime.UtcNow < timeout)
            Thread.Sleep(10);

        Equal<int?>(10, actor.State.Player.Gold);
        Equal<int?>(4, actor.State.Player.Level);
        Equal(2, actor.GetEventsSnapshot().Count);
    }
    finally
    {
        actor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}


static void ConditionalCache304()
{
    var temp = Path.Combine(Path.GetTempPath(), "astraltft-cache-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var handler = new TestHttpHandler();
        using var http = new HttpClient(handler);
        var cache = new ConditionalHttpCache(http, temp);
        var uri = new Uri("https://example.invalid/test.json");

        var first = cache.GetStringAsync(uri, "test", TimeSpan.FromDays(1)).AsTask().GetAwaiter().GetResult();
        var second = cache.GetStringAsync(uri, "test", TimeSpan.FromDays(1)).AsTask().GetAwaiter().GetResult();

        Equal("{\"ok\":true}", first.Content);
        Equal(first.Content, second.Content);
        True(!first.FromCache, "Initial successful response should be network-backed.");
        True(second.FromCache, "304 response should reuse the cached payload.");
        True(handler.SawConditionalHeader, "Second request should include If-None-Match.");
    }
    finally
    {
        try { Directory.Delete(temp, recursive: true); } catch { }
    }
}


static void RankBlend()
{
    var weights = RankBlendPolicy.Calculate(TftRankTier.Emerald, new[]
    {
        new RankBucketCandidate("emerald+", TftRankTier.Emerald, 80_000, .95, IncludesUserTier: true),
        new RankBucketCandidate("diamond+", TftRankTier.Diamond, 60_000, .95),
        new RankBucketCandidate("master+", TftRankTier.Master, 35_000, .96),
        new RankBucketCandidate("challenger", TftRankTier.Challenger, 800, .97)
    });

    True(Math.Abs(weights.Sum(x => x.Weight) - 1.0) < 1e-9, "Rank weights should normalize to one.");
    True(weights.First(x => x.Id == "emerald+").Weight > weights.First(x => x.Id == "challenger").Weight,
        "Tiny Challenger sample should not dominate a mature player-relevant bucket.");
    True(weights.First(x => x.Id == "master+").Weight > 0, "High-Elo signal should remain represented.");
}


static void EmergingSignal()
{
    var evidence = new[]
    {
        new MetricTrendEvidence("avg-placement", new TrendSignal(TrendDirection.Falling, .55, .85, 4.0, 4.4, ""), HigherIsBetter: false, Importance: 1.5),
        new MetricTrendEvidence("top4", new TrendSignal(TrendDirection.Rising, .45, .88, .61, .54, ""), HigherIsBetter: true, Importance: 1.5),
        new MetricTrendEvidence("play-rate", new TrendSignal(TrendDirection.Rising, .35, .80, .08, .05, ""), HigherIsBetter: true, Importance: .5)
    };

    var signal = EmergingMetaSignalBuilder.Build(evidence);
    Equal(EmergingMetaState.Emerging, signal.State);
    True(signal.Confidence >= .45, "Agreement across reliable metrics should clear confidence gate.");
}


static void GenericReviewSuppressed()
{
    var review = new RoundReview(
        new StagePoint(3, 5),
        "yi-reroll",
        CompArchetype.ThreeCostReroll,
        "Consider economy",
        "You may want to think about your economy and board.",
        ReviewSeverity.Info,
        .95,
        new[]
        {
            new ReviewEvidence("gold", "Gold", "42", .99),
            new ReviewEvidence("hp", "HP", "76", .99)
        },
        ReviewAvailability.PostGameDetailed);

    True(!RoundReviewSpecificityGate.ShouldDisplay(review), "Generic filler should not reach the companion window.");
}

static void SpecificReviewAccepted()
{
    var review = new RoundReview(
        new StagePoint(4, 1),
        "fast8-ap",
        CompArchetype.FastEight,
        "22g roll at 4-1 delayed Level 8",
        "The board was already stable at 74 HP with both frontline upgrades. Spending 22g on marginal upgrades moved the expected Level 8 timing back roughly one round, which conflicts with this line's primary Fast 8 win condition.",
        ReviewSeverity.Important,
        .90,
        new[]
        {
            new ReviewEvidence("gold-spent", "Roll spend", "22g", .93),
            new ReviewEvidence("hp", "HP", "74", .99),
            new ReviewEvidence("frontline", "Frontline", "2 upgraded tanks", .88),
            new ReviewEvidence("timing", "Expected timing", "Level 8 next round", .84)
        },
        ReviewAvailability.PostGameDetailed,
        Alternative: "Preserve the roll bank and level on the next timing window.");

    True(RoundReviewSpecificityGate.ShouldDisplay(review), "Concrete comp-specific review should clear the gate.");
}


static void PersonalizationMatures()
{
    var small = PersonalizationWeightPolicy.Calculate(8, .9, .8);
    var mature = PersonalizationWeightPolicy.Calculate(120, .9, .8);
    True(mature.Weight > small.Weight, "Larger history should earn more personalization authority.");
    True(mature.Weight <= .22, "Personal history must remain a bounded adjustment by default.");
}


static void CompResolverAmbiguity()
{
    var profiles = new Dictionary<string, CompPlaystyleProfile>(StringComparer.OrdinalIgnoreCase);
    var result = CompPlaystyleResolver.Resolve(new[]
    {
        new CompDirectionProbability("line-a", 38, .9),
        new CompDirectionProbability("line-b", 34, .9),
        new CompDirectionProbability("line-c", 28, .85)
    }, profiles);

    True(result.IsAmbiguous, "Closely split early directions should remain ambiguous.");
    True(result.Primary is null, "Ambiguous state should not hard-lock a playstyle profile.");
    True(Math.Abs(result.Directions.Sum(x => x.Probability) - 1.0) < 1e-9, "Direction probabilities should normalize.");
}


static void LiveRecommendationGated()
{
    True(!PresentationPolicy.CanPresent(AnalysisContentKind.StateAwareRecommendation, MatchLifecycle.InProgress),
        "State-aware recommendation must not render during active match.");
    True(PresentationPolicy.CanPresent(AnalysisContentKind.StateAwareRecommendation, MatchLifecycle.Finished),
        "Post-game state-aware analysis should be available.");
}


static void PatchVersionParsing()
{
    True(TftPatchVersion.TryParse("18.1b", out var patch), "Patch suffix should not break numeric parsing.");
    Equal(new TftPatchVersion(18, 1), patch);
}

static void LayoutRegistrySelects()
{
    var profiles = new[]
    {
        new LayoutProfile("set17", "TFT-PC", "17.1", "17.9", 1920, 1080, Array.Empty<LayoutRegion>(), IsProvisional: false),
        new LayoutProfile("set18", "TFT-PC", "18.1", "18.4", 1920, 1080, Array.Empty<LayoutRegion>(), IsProvisional: false)
    };
    var selected = new LayoutProfileRegistry(profiles).Select("TFT-PC", "18.1", 1920, 1080);
    Equal("set18", selected.Profile!.Id);
    True(!selected.RequiresCalibration, "Exact stable profile should not require calibration.");
}


static void CpuSnapshotCopiesRoi()
{
    var width = 4;
    var height = 3;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var offset = (y * width + x) * 4;
        pixels[offset] = (byte)(x + y * 10);
        pixels[offset + 1] = 2;
        pixels[offset + 2] = 3;
        pixels[offset + 3] = 255;
    }

    var frame = new CapturedFrame(7, DateTimeOffset.UtcNow, width, height,
        new Bgra32FrameBuffer(width, height, width * 4, pixels));
    var factory = new CpuBgraRegionSnapshotFactory();
    using var snapshot = (Bgra32RegionSnapshot)factory.Create(frame, new RegionOfInterest("shop", 1, 1, 2, 2));

    Equal(2, snapshot.Width);
    Equal(2, snapshot.Height);
    Equal(8, snapshot.Stride);
    Equal((byte)11, snapshot.Pixels.Span[0]);
    Equal((byte)12, snapshot.Pixels.Span[4]);
    Equal((byte)21, snapshot.Pixels.Span[8]);
}


static void RecognitionQueueCoalesces()
{
    using var queue = new CoalescingRecognitionQueue(capacity: 4);
    var detector = new FakeDetector("shop-detector", new[] { "shop" });
    var first = new TrackingSnapshot("shop", 1, DateTimeOffset.UtcNow);
    var second = new TrackingSnapshot("shop", 2, DateTimeOffset.UtcNow.AddMilliseconds(1));

    True(queue.Enqueue(new RecognitionWorkItem(detector, first, RecognitionPriority.Important, .5, DateTimeOffset.UtcNow)),
        "First work item should enqueue.");
    True(queue.Enqueue(new RecognitionWorkItem(detector, second, RecognitionPriority.Important, .8, DateTimeOffset.UtcNow.AddMilliseconds(1))),
        "Newer same-region work should replace pending work.");

    True(first.IsDisposed, "Replaced snapshot must be disposed immediately.");
    Equal(1, queue.Count);

    var dequeued = queue.DequeueAsync().AsTask().GetAwaiter().GetResult();
    try
    {
        Equal(2L, dequeued.Snapshot.FrameSequence);
    }
    finally
    {
        dequeued.Snapshot.Dispose();
    }
}

static void RecognitionQueueProtectsPriority()
{
    using var queue = new CoalescingRecognitionQueue(capacity: 1);
    var immediateDetector = new FakeDetector("augment-detector", new[] { "augment" });
    var backgroundDetector = new FakeDetector("board-detector", new[] { "board" });
    var augment = new TrackingSnapshot("augment", 1, DateTimeOffset.UtcNow);
    var board = new TrackingSnapshot("board", 2, DateTimeOffset.UtcNow.AddMilliseconds(1));

    True(queue.Enqueue(new RecognitionWorkItem(immediateDetector, augment, RecognitionPriority.Immediate, .4, DateTimeOffset.UtcNow)),
        "Immediate work should enqueue.");
    True(!queue.Enqueue(new RecognitionWorkItem(backgroundDetector, board, RecognitionPriority.Background, 1, DateTimeOffset.UtcNow.AddMilliseconds(1))),
        "Lower-priority work must not evict an augment decision.");

    board.Dispose();
    var dequeued = queue.DequeueAsync().AsTask().GetAwaiter().GetResult();
    try
    {
        Equal("augment", dequeued.Snapshot.RegionId);
    }
    finally
    {
        dequeued.Snapshot.Dispose();
    }
}

static void DetectorCircuitBreaker()
{
    var tracker = new DetectorHealthTracker();
    var detector = new FakeDetector("shop-detector", new[] { "shop" }, maxFailures: 2, cooldown: TimeSpan.FromSeconds(5));
    var descriptor = detector.Descriptor;
    var now = DateTimeOffset.UtcNow;

    tracker.RecordFailure(descriptor, now, "first");
    True(tracker.CanRun(descriptor, now), "One failure should degrade, not trip the detector.");
    tracker.RecordFailure(descriptor, now.AddMilliseconds(1), "second");
    True(!tracker.CanRun(descriptor, now.AddSeconds(1)), "Failure threshold should start cooldown.");
    True(tracker.CanRun(descriptor, now.AddSeconds(6)), "Detector should be retried after cooldown.");
    tracker.RecordSuccess(descriptor.Id, now.AddSeconds(6));
    Equal(DetectorHealthState.Healthy, tracker.Snapshot(descriptor.Id).State);
}

static void RecognitionDispatcherSkipsStale()
{
    var detector = new FakeDetector("shop-detector", new[] { "shop" }, staleAfter: TimeSpan.FromMilliseconds(20));
    var batchReady = new ManualResetEventSlim(false);
    RecognitionBatch? observed = null;

    var dispatcher = new RecognitionDispatcher(workerCount: 1, queueCapacity: 4);
    dispatcher.BatchCompleted += (_, batch) =>
    {
        observed = batch;
        batchReady.Set();
    };
    dispatcher.Start();

    try
    {
        var stale = new TrackingSnapshot("shop", 1, DateTimeOffset.UtcNow.AddSeconds(-1));
        True(dispatcher.Submit(detector, stale, RecognitionPriority.Important, .9, DateTimeOffset.UtcNow),
            "Stale work should enter queue and be rejected by execution freshness gate.");
        True(batchReady.Wait(TimeSpan.FromSeconds(2)), "Expected recognition completion callback.");
        Equal(RecognitionBatchStatus.SkippedStale, observed!.Status);
        Equal(0, detector.CallCount);
    }
    finally
    {
        dispatcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        batchReady.Dispose();
    }
}

static void RecognitionResultGateRejectsOld()
{
    var gate = new RecognitionResultSequenceGate();
    var now = DateTimeOffset.UtcNow;
    var newest = new RecognitionBatch("shop-detector", "shop", 12, now, now,
        RecognitionBatchStatus.NoObservation, Array.Empty<RecognitionObservation>());
    var older = newest with { FrameSequence = 11 };
    var duplicate = newest with { CompletedAt = now.AddMilliseconds(2) };

    True(gate.TryAccept(newest), "Newest result should be accepted.");
    True(!gate.TryAccept(older), "Older result must not overwrite newer state.");
    True(!gate.TryAccept(duplicate), "Duplicate sequence must not be applied twice.");
    Equal<long?>(12, gate.LatestSequence("shop-detector", "shop"));
}


static void DetectorRegistryRejectsDuplicates()
{
    var a = new FakeDetector("a", new[] { "shop" });
    var b = new FakeDetector("b", new[] { "shop" });
    var threw = false;
    try
    {
        _ = new RecognitionDetectorRegistry(new IRegionObservationDetector[] { a, b });
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }

    True(threw, "A region should have one composite detector owner.");
}

static void FramePumpDisposesCaptureLease()
{
    var lease = new TrackingDisposable();
    var pixels = Enumerable.Repeat((byte)80, 8 * 8 * 4).ToArray();
    var frame = new CapturedFrame(
        1,
        DateTimeOffset.UtcNow,
        8,
        8,
        new Bgra32FrameBuffer(8, 8, 8 * 4, pixels),
        lease);

    var frameSource = new FakeFrameSource(new[] { frame });
    var detector = new FakeDetector("shop-detector", new[] { "shop" });
    var registry = new RecognitionDetectorRegistry(new[] { detector });
    var dispatcher = new RecognitionDispatcher(workerCount: 1, queueCapacity: 4);
    dispatcher.Start();

    try
    {
        var router = new RecognitionFrameRouter(registry, new CpuBgraRegionSnapshotFactory(), dispatcher);
        var selector = new ChangedRegionSelector(new GridLumaRegionChangeDetector(2, 2, .01));
        var budget = new FixedRecognitionPumpBudgetProvider(new RecognitionPumpBudget(2, 2));
        var pump = new RecognitionFramePump(
            frameSource,
            selector,
            router,
            _ => new[]
            {
                new RecognitionRegion(
                    new RegionOfInterest("shop", 0, 0, 8, 8),
                    RecognitionPriority.Important,
                    TimeSpan.Zero)
            },
            budget);

        pump.RunAsync().GetAwaiter().GetResult();
        True(lease.IsDisposed, "Capture frame resource lease must be released after routing.");
        True(frameSource.Started && frameSource.Stopped, "Frame source lifecycle should be balanced.");
    }
    finally
    {
        dispatcher.DisposeAsync().AsTask().GetAwaiter().GetResult();
        frameSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}


static CapturedFrame Frame(long sequence, int width, int height, byte value, DateTimeOffset? at = null)
{
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < pixels.Length; i += 4)
    {
        pixels[i] = value;
        pixels[i + 1] = value;
        pixels[i + 2] = value;
        pixels[i + 3] = 255;
    }

    return new CapturedFrame(
        sequence,
        at ?? DateTimeOffset.UtcNow.AddMilliseconds(sequence),
        width,
        height,
        new Bgra32FrameBuffer(width, height, width * 4, pixels));
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

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void CorpusHashIsDeterministic()
{
    byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255];
    var first = RegionCorpusHasher.ComputeHash(2, 1, 8, pixels);
    var second = RegionCorpusHasher.ComputeHash(2, 1, 8, pixels);
    Equal(first, second);
    Equal(64, first.Length);
    True(first.All(c => char.IsAsciiHexDigitLower(c) || char.IsDigit(c)), "Hash must be lowercase hexadecimal.");
    True(first != RegionCorpusHasher.ComputeHash(1, 2, 4, pixels), "Geometry must participate in the hash.");
}

static void CorpusContractsRejectUnsafeGeometry()
{
    Throws<ArgumentOutOfRangeException>(() => RegionCorpusHasher.ComputeHash(0, 1, 4, new byte[4]));
    Throws<ArgumentOutOfRangeException>(() => RegionCorpusHasher.ComputeHash(4097, 1, 4097 * 4, new byte[4097 * 4]));
    Throws<ArgumentException>(() => RegionCorpusHasher.ComputeHash(2, 1, 8, new byte[7]));
}

static void CorpusRoundTripsInOrder()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    var firstCapturedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    var secondCapturedAt = firstCapturedAt.AddMilliseconds(17);
    byte[] firstPixels = [1, 2, 3, 255, 4, 5, 6, 255];
    byte[] secondPixels = [7, 8, 9, 255, 10, 11, 12, 255];

    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, firstCapturedAt, 2, 1, 8, firstPixels, RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-2", 102, secondCapturedAt, 2, 1, 8, secondPixels, RegionCorpusSourceKind.ImportedFrame))
        .AsTask().GetAwaiter().GetResult();

    var snapshots = ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult();
    try
    {
        Equal(2, snapshots.Count);
        AssertSnapshot(snapshots[0], "shop-slot-1", 101, firstCapturedAt, firstPixels);
        AssertSnapshot(snapshots[1], "shop-slot-2", 102, secondCapturedAt, secondPixels);
    }
    finally
    {
        foreach (var snapshot in snapshots)
            snapshot.Dispose();
    }
}

static void CorpusStoreDeduplicatesBlobs()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255];
    var capturedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    var first = store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, capturedAt, 2, 1, 8, pixels, RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    var second = store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-2", 102, capturedAt.AddMilliseconds(1), 2, 1, 8, pixels, RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();

    True(first.BlobCreated, "First write should create the content blob.");
    True(!second.BlobCreated, "Second write with identical pixels should reuse the content blob.");
    Equal(first.ContentHash, second.ContentHash);
    Equal(2, File.ReadLines(Path.Combine(temporary.Path, "observations.jsonl")).Count());
    Equal(1, Directory.EnumerateFiles(Path.Combine(temporary.Path, "blobs"), "*.bgra").Count());
}

static void CorpusReaderRejectsHashMismatch()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255];
    var result = store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        2, 1, 8, pixels, RegionCorpusSourceKind.LiveCapture)).AsTask().GetAwaiter().GetResult();
    var blobPath = Path.Combine(temporary.Path, "blobs", result.ContentHash + ".bgra");
    var corrupt = File.ReadAllBytes(blobPath);
    corrupt[0] = 99;
    File.WriteAllBytes(blobPath, corrupt);

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult());
}

static void CorpusReaderIgnoresIncompleteTail()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    byte[] firstPixels = [1, 2, 3, 255, 4, 5, 6, 255];
    var capturedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, capturedAt, 2, 1, 8, firstPixels, RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    File.AppendAllText(Path.Combine(temporary.Path, "observations.jsonl"), "{\"schemaVersion\":");

    var reader = new RegionCorpusReader(temporary.Path);
    var snapshots = ReadCorpusAsync(reader).GetAwaiter().GetResult();
    try
    {
        Equal(1, snapshots.Count);
        AssertSnapshot(snapshots[0], "shop-slot-1", 101, capturedAt, firstPixels);
        Equal(1, reader.IgnoredIncompleteTailCount);
    }
    finally
    {
        foreach (var snapshot in snapshots)
            snapshot.Dispose();
    }

    using var malformedTemporary = new TemporaryDirectory();
    var malformedStore = new RegionCorpusStore(malformedTemporary.Path, "foundation-tests");
    malformedStore.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 201, capturedAt, 2, 1, 8, firstPixels, RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    File.AppendAllText(Path.Combine(malformedTemporary.Path, "observations.jsonl"), "{not-json}\n");
    malformedStore.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-2", 202, capturedAt.AddMilliseconds(1), 2, 1, 8,
        [7, 8, 9, 255, 10, 11, 12, 255], RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(malformedTemporary.Path)).GetAwaiter().GetResult());
}

static void CorpusReaderRejectsCompleteFinalJsonCorruption()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        2, 1, 8, [1, 2, 3, 255, 4, 5, 6, 255], RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    File.AppendAllText(Path.Combine(temporary.Path, "observations.jsonl"), "{not-json}" + Environment.NewLine);

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult());
}

static void CorpusReaderRejectsInvalidFinalMetadata()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        2, 1, 8, [1, 2, 3, 255, 4, 5, 6, 255], RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    File.AppendAllText(
        Path.Combine(temporary.Path, "observations.jsonl"),
        "{\"schemaVersion\":2,\"contentHash\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"regionId\":\"shop-slot-2\",\"frameSequence\":102,\"capturedAtUtc\":\"2026-08-28T12:00:00+00:00\",\"width\":2,\"height\":1,\"stride\":8,\"sourceKind\":\"LiveCapture\"}" + Environment.NewLine);

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult());
}

static void CorpusReaderNormalizesMissingBlobDirectory()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        2, 1, 8, [1, 2, 3, 255, 4, 5, 6, 255], RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    Directory.Delete(Path.Combine(temporary.Path, "blobs"), recursive: true);

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult());
}

static void CorpusReaderRejectsTerminatedTrailingJsonComma()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        2, 1, 8, [1, 2, 3, 255, 4, 5, 6, 255], RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    File.AppendAllText(Path.Combine(temporary.Path, "observations.jsonl"), "{\"schemaVersion\":1," + Environment.NewLine);

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult());
}

static void CorpusReaderRejectsTerminatedUnterminatedJsonString()
{
    using var temporary = new TemporaryDirectory();
    var store = new RegionCorpusStore(temporary.Path, "foundation-tests");
    store.WriteAsync(new RegionCorpusWriteRequest(
        "shop-slot-1", 101, new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        2, 1, 8, [1, 2, 3, 255, 4, 5, 6, 255], RegionCorpusSourceKind.LiveCapture))
        .AsTask().GetAwaiter().GetResult();
    File.AppendAllText(Path.Combine(temporary.Path, "observations.jsonl"), "{\"regionId\":\"unterminated" + Environment.NewLine);

    Throws<InvalidDataException>(() => ReadCorpusAsync(new RegionCorpusReader(temporary.Path)).GetAwaiter().GetResult());
}

static async Task<List<Bgra32RegionSnapshot>> ReadCorpusAsync(RegionCorpusReader reader)
{
    var snapshots = new List<Bgra32RegionSnapshot>();
    await foreach (var snapshot in reader.ReadAsync())
        snapshots.Add(snapshot);
    return snapshots;
}

static void AssertSnapshot(
    Bgra32RegionSnapshot snapshot,
    string expectedRegionId,
    long expectedSequence,
    DateTimeOffset expectedCapturedAt,
    byte[] expectedPixels)
{
    Equal(expectedRegionId, snapshot.RegionId);
    Equal(expectedSequence, snapshot.FrameSequence);
    Equal(expectedCapturedAt, snapshot.CapturedAt);
    Equal(2, snapshot.Width);
    Equal(1, snapshot.Height);
    Equal(8, snapshot.Stride);
    True(snapshot.Pixels.Span.SequenceEqual(expectedPixels), "Snapshot pixels should replay exactly.");
}



static void ShopStructureRecognition()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];

    // Dark baseline approximates a bought/empty shop card.
    FillBgra(pixels, stride, 0, 0, width, height, 12, 12, 6);

    var slots = ShopSlotRecognizer.ProjectSlots(width, height);
    Equal(5, slots.Count);
    PaintSyntheticShopHudFrame(pixels, stride, slots);

    // Leave slot 1 empty. Fill slots 2-5 with textured portrait signal plus
    // the exact Set 18 cost-bar color families seen in the calibration frame.
    PaintSyntheticShopUnit(pixels, stride, slots[1], 18, 53, 44); // 2-cost green
    PaintSyntheticShopUnit(pixels, stride, slots[2], 23, 37, 50); // 1-cost blue-grey
    PaintSyntheticShopUnit(pixels, stride, slots[3], 18, 53, 44); // 2-cost green
    PaintSyntheticShopUnit(pixels, stride, slots[4], 28, 31, 71); // 3-cost blue

    var recognizer = new ShopSlotRecognizer();
    var result = recognizer.Recognize(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(result.IsShopHudVisible, "A complete five-slot shop should pass the HUD visibility gate.");
    Equal(5, result.KnownSlotCount);
    Equal(ShopSlotOccupancy.Empty, result.Slots[0].Occupancy);
    Equal(0, result.Slots[0].CostTier);

    Equal(ShopSlotOccupancy.Unit, result.Slots[1].Occupancy);
    Equal(2, result.Slots[1].CostTier);

    Equal(ShopSlotOccupancy.Unit, result.Slots[2].Occupancy);
    Equal(1, result.Slots[2].CostTier);

    Equal(ShopSlotOccupancy.Unit, result.Slots[3].Occupancy);
    Equal(2, result.Slots[3].CostTier);

    Equal(ShopSlotOccupancy.Unit, result.Slots[4].Occupancy);
    Equal(3, result.Slots[4].CostTier);
}

static void PaintSyntheticShopUnit(
    byte[] pixels,
    int stride,
    RegionOfInterest slot,
    byte rBar,
    byte gBar,
    byte bBar)
{
    // High-variance checkerboard creates obvious portrait/trait visual structure.
    for (var y = slot.Y; y < slot.Y + slot.Height; y++)
    {
        for (var x = slot.X; x < slot.X + slot.Width; x++)
        {
            var bright = ((x / 8) + (y / 8)) % 2 == 0;
            var value = (byte)(bright ? 175 : 35);
            SetBgra(pixels, stride, x, y, value, value, value);
        }
    }

    var barY = slot.Y + (int)Math.Round(slot.Height * 0.80);
    FillBgra(
        pixels,
        stride,
        slot.X,
        barY,
        slot.Width,
        slot.Y + slot.Height - barY,
        rBar,
        gBar,
        bBar);
}

static void FillBgra(
    byte[] pixels,
    int stride,
    int x,
    int y,
    int width,
    int height,
    byte r,
    byte g,
    byte b)
{
    for (var yy = y; yy < y + height; yy++)
    {
        for (var xx = x; xx < x + width; xx++)
            SetBgra(pixels, stride, xx, yy, r, g, b);
    }
}

static void SetBgra(byte[] pixels, int stride, int x, int y, byte r, byte g, byte b)
{
    var offset = y * stride + x * 4;
    pixels[offset] = b;
    pixels[offset + 1] = g;
    pixels[offset + 2] = r;
    pixels[offset + 3] = 255;
}


static void ShopCostBarMedianResistsGlyphNoise()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    FillBgra(pixels, stride, 0, 0, width, height, 12, 12, 6);

    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    PaintSyntheticShopUnit(pixels, stride, slots[0], 24, 38, 50);  // 1-cost
    PaintSyntheticShopUnit(pixels, stride, slots[1], 96, 67, 24);  // 5-cost
    PaintSyntheticShopUnit(pixels, stride, slots[2], 28, 39, 75);  // 3-cost
    PaintSyntheticShopUnit(pixels, stride, slots[3], 76, 31, 86);  // 4-cost
    PaintSyntheticShopUnit(pixels, stride, slots[4], 104, 72, 25); // 5-cost

    PaintSparseNoise(pixels, stride, slots[1], 82, 28, 92);
    PaintSparseNoise(pixels, stride, slots[4], 23, 37, 50);

    var recognizer = new ShopSlotRecognizer();
    var result = recognizer.Recognize(new Bgra32FrameBuffer(width, height, stride, pixels));

    Equal(1, result.Slots[0].CostTier);
    Equal(5, result.Slots[1].CostTier);
    Equal(3, result.Slots[2].CostTier);
    Equal(4, result.Slots[3].CostTier);
    Equal(5, result.Slots[4].CostTier);
}

static void PaintSparseNoise(
    byte[] pixels,
    int stride,
    RegionOfInterest slot,
    byte r,
    byte g,
    byte b)
{
    ReadOnlySpan<double> xFractions = [0.12, 0.25, 0.40, 0.55, 0.70, 0.85];
    ReadOnlySpan<double> yFractions = [0.86, 0.90, 0.94];

    foreach (var yf in yFractions)
    {
        foreach (var xf in xFractions)
        {
            var x = Math.Clamp(slot.X + (int)Math.Round(slot.Width * xf), slot.X, slot.X + slot.Width - 1);
            var y = Math.Clamp(slot.Y + (int)Math.Round(slot.Height * yf), slot.Y, slot.Y + slot.Height - 1);
            SetBgra(pixels, stride, x, y, r, g, b);
        }
    }
}


static void ShopHudGateRejectsIncompleteFalsePositives()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    FillBgra(pixels, stride, 0, 0, width, height, 14, 14, 10);

    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    // This mirrors the bad pre-game behavior we observed: several card-like regions
    // happen to classify as tiers, but other slots are structurally unknown.
    PaintSyntheticShopUnit(pixels, stride, slots[0], 18, 53, 44); // 2-cost
    PaintSyntheticShopUnit(pixels, stride, slots[1], 96, 67, 24); // 5-cost
    PaintSyntheticShopUnit(pixels, stride, slots[3], 18, 53, 44); // 2-cost

    // Deliberately leave slots 3 and 5 as ambiguous non-empty texture.
    FillBgra(
        pixels,
        stride,
        slots[2].X,
        slots[2].Y,
        slots[2].Width,
        slots[2].Height,
        42,
        42,
        42);
    FillBgra(
        pixels,
        stride,
        slots[4].X,
        slots[4].Y,
        slots[4].Width,
        slots[4].Height,
        47,
        47,
        47);

    var recognizer = new ShopSlotRecognizer();
    var result = recognizer.Recognize(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(!result.IsShopHudVisible, "Incomplete/ambiguous slot structure must not activate the shop HUD.");
    True(result.KnownSlotCount < 5, "False-positive frame should not resolve all five slots.");
}


static void ShopHudRequiresFrameAnchors()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    FillBgra(pixels, stride, 0, 0, width, height, 14, 14, 10);

    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    // Five plausible card-like regions are intentionally not enough. Carousel,
    // loading, or board scenery can accidentally classify as costs; without the
    // repeated TFT shop-frame anchors recognition must remain inactive.
    PaintSyntheticShopUnit(pixels, stride, slots[0], 24, 38, 50);
    PaintSyntheticShopUnit(pixels, stride, slots[1], 96, 67, 24);
    PaintSyntheticShopUnit(pixels, stride, slots[2], 28, 39, 75);
    PaintSyntheticShopUnit(pixels, stride, slots[3], 76, 31, 86);
    PaintSyntheticShopUnit(pixels, stride, slots[4], 104, 72, 25);

    var recognizer = new ShopSlotRecognizer();
    var buffer = new Bgra32FrameBuffer(width, height, stride, pixels);
    var hud = recognizer.CheckHud(buffer);

    True(!hud.IsVisible, "Five card-like regions without shop-frame anchors must stay inactive.");
    True(!hud.SupportsHold, "Card-like regions without shop-frame anchors must not prolong a confirmed shop.");
}

static void PaintSyntheticShopHudFrame(
    byte[] pixels,
    int stride,
    IReadOnlyList<RegionOfInterest> slots)
{
    foreach (var slot in slots)
    {
        FillBgra(
            pixels,
            stride,
            slot.X,
            Math.Max(0, slot.Y - 2),
            slot.Width,
            7,
            16,
            22,
            24);
    }

    for (var i = 0; i < slots.Count - 1; i++)
    {
        var left = slots[i];
        var right = slots[i + 1];
        var gapX = left.X + left.Width;
        var gapWidth = Math.Max(1, right.X - gapX);
        var top = Math.Max(left.Y, right.Y) + 8;
        var bottom = Math.Min(left.Y + left.Height, right.Y + right.Height) - 8;

        FillBgra(
            pixels,
            stride,
            gapX,
            top,
            gapWidth,
            Math.Max(1, bottom - top),
            16,
            22,
            24);
    }
}

static void PaintShopHudAnchorCoverage(
    byte[] pixels,
    int stride,
    IReadOnlyList<RegionOfInterest> slots,
    double topBorderFraction,
    double separatorFraction)
{
    foreach (var slot in slots)
    {
        PaintBgraFraction(
            pixels,
            stride,
            slot.X,
            Math.Max(0, slot.Y - 2),
            slot.Width,
            7,
            topBorderFraction,
            0,
            60,
            80);
    }

    for (var i = 0; i < slots.Count - 1; i++)
    {
        var left = slots[i];
        var right = slots[i + 1];
        var gapX = left.X + left.Width;
        var gapWidth = Math.Max(1, right.X - gapX);
        var top = Math.Max(left.Y, right.Y) + 8;
        var bottom = Math.Min(left.Y + left.Height, right.Y + right.Height) - 8;

        PaintBgraFraction(
            pixels,
            stride,
            gapX,
            top,
            gapWidth,
            Math.Max(1, bottom - top),
            separatorFraction,
            0,
            60,
            80);
    }
}

static void PaintBgraFraction(
    byte[] pixels,
    int stride,
    int x,
    int y,
    int width,
    int height,
    double fraction,
    byte r,
    byte g,
    byte b)
{
    var pixelCount = (int)Math.Round(width * height * fraction, MidpointRounding.AwayFromZero);
    for (var index = 0; index < pixelCount; index++)
    {
        var xx = x + index % width;
        var yy = y + index / width;
        SetBgra(pixels, stride, xx, yy, r, g, b);
    }
}

static void PaintNeutralShopHudFrame(
    byte[] pixels,
    int stride,
    IReadOnlyList<RegionOfInterest> slots,
    byte topBorderValue,
    byte separatorValue)
{
    foreach (var slot in slots)
    {
        FillBgra(
            pixels,
            stride,
            slot.X,
            Math.Max(0, slot.Y - 2),
            slot.Width,
            7,
            topBorderValue,
            topBorderValue,
            topBorderValue);
    }

    for (var i = 0; i < slots.Count - 1; i++)
    {
        var left = slots[i];
        var right = slots[i + 1];
        var gapX = left.X + left.Width;
        var gapWidth = Math.Max(1, right.X - gapX);
        var top = Math.Max(left.Y, right.Y) + 8;
        var bottom = Math.Min(left.Y + left.Height, right.Y + right.Height) - 8;

        FillBgra(
            pixels,
            stride,
            gapX,
            top,
            gapWidth,
            Math.Max(1, bottom - top),
            separatorValue,
            separatorValue,
            separatorValue);
    }
}


static void ShopHudAcceptsGreyedChrome()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    FillBgra(pixels, stride, 0, 0, width, height, 18, 18, 18);

    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    // Draw the same repeated shop frame in a fully desaturated dark grey,
    // modelling TFT's unaffordable/temporarily muted visual state.
    PaintNeutralShopHudFrame(pixels, stride, slots, 34, 32);

    var recognizer = new ShopSlotRecognizer();
    var hud = recognizer.CheckHud(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(!hud.IsVisible, "Greyed shop chrome must not cold-start shop recognition.");
    True(hud.SupportsHold, "Greyed shop chrome must sustain an already-confirmed shop.");
    Equal(0, hud.TopBorderMatches);
    Equal(0, hud.SeparatorMatches);
    Equal(0d, hud.Confidence);
}

static void ShopHudAcceptsLowLumaGreyedChrome()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    FillBgra(pixels, stride, 0, 0, width, height, 8, 8, 8);

    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    // Luminance-preserving desaturation of calibrated RGB(16,22,24) chrome
    // is approximately RGB(21,21,21), below the previous neutral cutoff.
    PaintNeutralShopHudFrame(pixels, stride, slots, 21, 20);

    var recognizer = new ShopSlotRecognizer();
    var hud = recognizer.CheckHud(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(!hud.IsVisible, "Low-luma greyed chrome must not cold-start shop recognition.");
    True(hud.SupportsHold, "Low-luma greyed chrome must sustain an already-confirmed shop.");
}

static void ShopHudRejectsUniformNeutralScenery()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];

    // A flat dark-grey scene satisfies a color-only neutral predicate at every
    // projected anchor. It has no local frame contrast and is not shop evidence.
    FillBgra(pixels, stride, 0, 0, width, height, 34, 34, 34);

    var recognizer = new ShopSlotRecognizer();
    var hud = recognizer.CheckHud(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(!hud.IsVisible, "Uniform neutral scenery must not activate the shop HUD.");
    True(!hud.SupportsHold, "Uniform neutral scenery must not prolong a confirmed shop.");
}

static void ShopHudRejectsUniformTintedScenery()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var recognizer = new ShopSlotRecognizer();

    ReadOnlySpan<(byte R, byte G, byte B)> sceneryColors =
    [
        (34, 36, 36), // Capture noise around an otherwise neutral grey.
        (16, 22, 24)  // Exact chromatic frame color without frame structure.
    ];

    foreach (var (r, g, b) in sceneryColors)
    {
        var pixels = new byte[stride * height];
        FillBgra(pixels, stride, 0, 0, width, height, r, g, b);

        var hud = recognizer.CheckHud(new Bgra32FrameBuffer(width, height, stride, pixels));
        True(!hud.IsVisible, $"Uniform RGB({r},{g},{b}) scenery must not activate the shop HUD.");
        True(!hud.SupportsHold, $"Uniform RGB({r},{g},{b}) scenery must not prolong a confirmed shop.");
    }
}

static void ShopHudRejectsWeakFrameCoverage()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    // These ratios clear the regressed 0.24/0.14 thresholds but remain below
    // the calibrated 0.28/0.18 activation thresholds.
    PaintShopHudAnchorCoverage(pixels, stride, slots, 0.25, 0.15);

    var recognizer = new ShopSlotRecognizer();
    var hud = recognizer.CheckHud(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(!hud.IsVisible, "Sub-threshold frame coverage must not activate the shop HUD.");
    Equal(0, hud.TopBorderMatches);
    Equal(0, hud.SeparatorMatches);
}

static void ShopHudAcceptsCalibratedFrameCoverage()
{
    const int width = 1152;
    const int height = 239;
    const int stride = width * 4;
    var pixels = new byte[stride * height];
    var slots = ShopSlotRecognizer.ProjectSlots(width, height);

    PaintShopHudAnchorCoverage(pixels, stride, slots, 0.29, 0.19);

    var recognizer = new ShopSlotRecognizer();
    var hud = recognizer.CheckHud(new Bgra32FrameBuffer(width, height, stride, pixels));

    True(hud.IsVisible, "Coverage above the calibrated thresholds must activate the shop HUD.");
    Equal(5, hud.TopBorderMatches);
    Equal(4, hud.SeparatorMatches);
}

static void NormalizedWgcRoiProjectsDeterministically()
{
    var region = new WgcNormalizedRegion("shop", 0.20, 0.77, 0.60, 0.22);
    var projected = region.Project(1920, 1080);

    Equal("shop", projected.Id);
    Equal(384, projected.X);
    Equal(831, projected.Y);
    Equal(1152, projected.Width);
    Equal(239, projected.Height);
}

static void TftWindowSelectorRejectsFalsePositives()
{
    var candidates = new[]
    {
        new TftWindowCandidateSelector.Candidate(
            (nint)1, "firefox", "Blossom Comp Guide [Set 18] | TFT Flow — Mozilla Firefox", 1920, 1040, false),
        new TftWindowCandidateSelector.Candidate(
            (nint)2, "TFTAcademy", "TFTAcademy", 1080, 1194, false),
        new TftWindowCandidateSelector.Candidate(
            (nint)3, "TFTClient-Win64-Shipping", "TFT  ", 1920, 1080, false)
    };

    Equal(0, TftWindowCandidateSelector.Score("firefox", "TFT Flow"));
    Equal(0, TftWindowCandidateSelector.Score("TFTAcademy", "TFTAcademy"));

    var selected = TftWindowCandidateSelector.ChooseBest(candidates);
    True(selected is not null, "Expected the real TFT client to be selected.");
    Equal((nint)3, selected!.Window.Hwnd);
    Equal("TFTClient-Win64-Shipping", selected.Window.ProcessName);
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astraltft-foundation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

sealed class TrackingDisposable : IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

sealed class FakeFrameSource : IFrameSource
{
    private readonly IReadOnlyList<CapturedFrame> _frames;

    public FakeFrameSource(IReadOnlyList<CapturedFrame> frames) => _frames = frames;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        Started = true;
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in _frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
            await Task.Yield();
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        Stopped = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}


sealed class TrackingSnapshot : IRegionSnapshot
{
    public TrackingSnapshot(string regionId, long frameSequence, DateTimeOffset capturedAt)
    {
        RegionId = regionId;
        FrameSequence = frameSequence;
        CapturedAt = capturedAt;
    }

    public string RegionId { get; }
    public long FrameSequence { get; }
    public DateTimeOffset CapturedAt { get; }
    public int Width => 8;
    public int Height => 8;
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}


sealed class FakeDetector : IRegionObservationDetector
{
    public FakeDetector(
        string id,
        IEnumerable<string> regions,
        TimeSpan? staleAfter = null,
        int maxFailures = 4,
        TimeSpan? cooldown = null)
    {
        Descriptor = new RecognitionDetectorDescriptor(
            id,
            new HashSet<string>(regions, StringComparer.OrdinalIgnoreCase),
            staleAfter ?? TimeSpan.FromSeconds(1),
            maxFailures,
            cooldown ?? TimeSpan.FromSeconds(1));
    }

    public RecognitionDetectorDescriptor Descriptor { get; }
    public int CallCount { get; private set; }

    public ValueTask<RecognitionBatch> DetectAsync(IRegionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return ValueTask.FromResult(RecognitionBatch.Empty(
            Descriptor.Id,
            snapshot,
            DateTimeOffset.UtcNow,
            RecognitionBatchStatus.NoObservation));
    }
}


sealed class TestHttpHandler : HttpMessageHandler
{
    private int _count;
    public bool SawConditionalHeader { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _count++;
        if (_count == 1)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
            return Task.FromResult(response);
        }

        SawConditionalHeader = request.Headers.IfNoneMatch.Any();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
    }
}
