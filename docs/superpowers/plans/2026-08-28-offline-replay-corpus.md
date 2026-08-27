# Offline Replay Corpus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an opt-in, non-blocking shop-slot corpus recorder and deterministic offline BGRA32 replay reader.

**Architecture:** The Capture project owns a versioned content-addressed corpus, strict validation, a single-writer bounded recorder, and replay through the existing `Bgra32RegionSnapshot` type. The WPF app creates the recorder only when an absolute `ASTRALTFT_CORPUS_DIRECTORY` is configured and submits the five existing shop-slot ROIs only after confirmed meaningful shop changes.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json`, `System.Security.Cryptography`, `System.Threading.Channels`, existing AstralTFT capture contracts, executable foundation self-tests.

**Spec:** `docs/superpowers/specs/2026-08-28-offline-replay-corpus-design.md`

## Global Constraints

- Corpus schema version is exactly `1` and pixel format is exactly `Bgra32`.
- Canonical samples use packed BGRA32 with `Stride == Width * 4`.
- Dimensions are positive and at most 4096 by 4096; canonical bytes are at most 64 MiB.
- Content IDs are lowercase SHA-256 over marker, little-endian geometry, and pixels.
- Queue capacity defaults to 16 and enqueue never waits.
- Normal mode performs no corpus allocation, hashing, or I/O.
- Developer capture stores selected TFT ROIs locally and never reads process memory or captures the desktop.
- Existing untracked `apply-astraltft-*.ps1` files remain untouched and unstaged.

---

### Task 1: Canonical corpus samples and hashing

**Files:**
- Create: `src/AstralTFT.Capture/Replay/RegionCorpusContracts.cs`
- Create: `src/AstralTFT.Capture/Replay/RegionCorpusHasher.cs`
- Modify: `tests/AstralTFT.Foundation.Tests/Program.cs`

**Interfaces:**
- Produces: `RegionCorpusObservation`, `RegionCorpusHeader`, `RegionCorpusSourceKind`, `RegionCorpusWriteRequest`, and `RegionCorpusHasher.ComputeHash(int, int, int, ReadOnlySpan<byte>)`.
- Consumes: `Bgra32RegionSnapshot` validation conventions from `RecognitionContracts.cs`.

- [ ] **Step 1: Register failing canonical-hash tests**

Add these runner entries and functions to `tests/AstralTFT.Foundation.Tests/Program.cs`:

```csharp
("Corpus hash is deterministic and geometry-sensitive", CorpusHashIsDeterministic),
("Corpus contracts reject unsafe geometry", CorpusContractsRejectUnsafeGeometry),

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
```

Add a generic `Throws<TException>(Action action)` assertion beside `True` and `Equal`.

- [ ] **Step 2: Run foundation tests and verify RED**

Run:

```powershell
dotnet run --project .\tests\AstralTFT.Foundation.Tests\AstralTFT.Foundation.Tests.csproj -c Release --no-restore
```

Expected: compilation fails because the replay contracts and hasher do not exist.

- [ ] **Step 3: Implement contracts and canonical hashing**

Create public immutable records with these signatures:

```csharp
public enum RegionCorpusSourceKind { LiveCapture, ImportedFrame }

public sealed record RegionCorpusHeader(
    int SchemaVersion,
    string PixelFormat,
    DateTimeOffset CreatedAtUtc,
    string CreatedByVersion);

public sealed record RegionCorpusObservation(
    int SchemaVersion,
    string ContentHash,
    string RegionId,
    long FrameSequence,
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height,
    int Stride,
    RegionCorpusSourceKind SourceKind);

public sealed record RegionCorpusWriteRequest(
    string RegionId,
    long FrameSequence,
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height,
    int Stride,
    byte[] Pixels,
    RegionCorpusSourceKind SourceKind);
```

Implement `RegionCorpusHasher` with constants `MaxDimension = 4096` and `MaxCanonicalBytes = 64 * 1024 * 1024`. Validate packed stride, exact byte length, and bounds. Hash `"AstralTFT-BGRA32-v1\0"`, three little-endian `int`s written with `BinaryPrimitives.WriteInt32LittleEndian`, then pixels using `IncrementalHash`. Return `Convert.ToHexString(hash).ToLowerInvariant()`.

- [ ] **Step 4: Run foundation tests and verify GREEN**

Expected: both new corpus tests pass and the existing suite remains green.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- src/AstralTFT.Capture/Replay/RegionCorpusContracts.cs src/AstralTFT.Capture/Replay/RegionCorpusHasher.cs tests/AstralTFT.Foundation.Tests/Program.cs
git commit -m "Add canonical replay corpus samples"
```

---

### Task 2: Atomic content-addressed store and deterministic reader

**Files:**
- Create: `src/AstralTFT.Capture/Replay/IRegionCorpusSink.cs`
- Create: `src/AstralTFT.Capture/Replay/RegionCorpusStore.cs`
- Create: `src/AstralTFT.Capture/Replay/RegionCorpusReader.cs`
- Modify: `tests/AstralTFT.Foundation.Tests/Program.cs`

**Interfaces:**
- Consumes: Task 1 records and `RegionCorpusHasher.ComputeHash`.
- Produces: `IRegionCorpusSink.WriteAsync(RegionCorpusWriteRequest, CancellationToken)`, `RegionCorpusWriteResult`, `RegionCorpusStore`, and `RegionCorpusReader.ReadAsync` yielding `Bgra32RegionSnapshot`.

- [ ] **Step 1: Write failing round-trip, dedupe, corruption, and tail tests**

Register these tests:

```csharp
("Corpus store replays exact snapshots in order", CorpusRoundTripsInOrder),
("Corpus store deduplicates blobs", CorpusStoreDeduplicatesBlobs),
("Corpus reader rejects hash mismatch", CorpusReaderRejectsHashMismatch),
("Corpus reader ignores only incomplete final line", CorpusReaderIgnoresIncompleteTail),
```

Use a `TemporaryDirectory` test helper under `Path.GetTempPath()`. Write two observations with literal 2×1 pixel arrays and distinct sequence/timestamps; assert replay order and every property/pixel. Write identical pixels twice; assert two observations and exactly one `.bgra` blob. Replace one blob byte and assert `InvalidDataException`. Append `{"schemaVersion":` as the final log fragment and assert valid observations replay plus `IgnoredIncompleteTailCount == 1`; place malformed JSON before a final valid line and assert failure.

- [ ] **Step 2: Run foundation tests and verify RED**

Expected: compilation fails because store and reader types do not exist.

- [ ] **Step 3: Implement the sink and store**

Define:

```csharp
public sealed record RegionCorpusWriteResult(string ContentHash, bool BlobCreated);

public interface IRegionCorpusSink
{
    ValueTask<RegionCorpusWriteResult> WriteAsync(
        RegionCorpusWriteRequest request,
        CancellationToken cancellationToken = default);
}
```

`RegionCorpusStore` constructor resolves an absolute root, creates `blobs`, atomically creates `corpus.json`, or validates an existing header. `WriteAsync` validates the request, calculates the hash, writes a flushed temporary blob and atomically moves it, then appends one compact camel-case JSON observation plus `Environment.NewLine` and flushes. Temporary names use `Path.GetRandomFileName`; all blob paths use validated 64-character lowercase hashes only.

- [ ] **Step 4: Implement replay validation and ordering**

`RegionCorpusReader` validates `corpus.json`, uses a one-line lookahead so malformed non-final JSON fails while a malformed final line is ignored, validates every observation, reads the exact expected blob length, recomputes the content hash, and yields a new owned `Bgra32RegionSnapshot`. Increment `IgnoredIncompleteTailCount` only for an invalid final non-empty line.

- [ ] **Step 5: Run foundation tests and verify GREEN**

Expected: exact round-trip, ordering, dedupe, corruption, and tail tests pass.

- [ ] **Step 6: Commit Task 2**

```powershell
git add -- src/AstralTFT.Capture/Replay/IRegionCorpusSink.cs src/AstralTFT.Capture/Replay/RegionCorpusStore.cs src/AstralTFT.Capture/Replay/RegionCorpusReader.cs tests/AstralTFT.Foundation.Tests/Program.cs
git commit -m "Add deterministic replay corpus storage"
```

---

### Task 3: Bounded non-blocking recorder

**Files:**
- Create: `src/AstralTFT.Capture/Replay/BoundedRegionCorpusRecorder.cs`
- Modify: `tests/AstralTFT.Foundation.Tests/Program.cs`

**Interfaces:**
- Consumes: `IRegionCorpusSink`, `RegionCorpusWriteRequest`, and `Bgra32RegionSnapshot`.
- Produces: `BoundedRegionCorpusRecorder.TryRecord(Bgra32RegionSnapshot, RegionCorpusSourceKind)`, `RegionCorpusRecorderMetrics`, and drain-on-`DisposeAsync`.

- [ ] **Step 1: Write failing bounded-recorder tests**

Register:

```csharp
("Corpus recorder rejects full queue without blocking", CorpusRecorderRejectsFullQueue),
("Corpus recorder drains accepted snapshots", CorpusRecorderDrainsAcceptedSnapshots),
```

Add a `BlockingCorpusSink` whose first `WriteAsync` signals entry and waits on a `TaskCompletionSource`. With capacity `1`, enqueue one active write, one pending write, and assert the third call returns `false` within the same synchronous call. Release the sink, dispose the recorder, and assert metrics: accepted `2`, dropped `1`, written `2`, pending `0`. A second sink returns `BlobCreated=false` for duplicate content; assert deduplicated metrics.

- [ ] **Step 2: Run foundation tests and verify RED**

Expected: compilation fails because `BoundedRegionCorpusRecorder` does not exist.

- [ ] **Step 3: Implement the recorder**

Define metrics:

```csharp
public sealed record RegionCorpusRecorderMetrics(
    long Accepted,
    long Dropped,
    long Written,
    long Deduplicated,
    long Failed,
    long Pending,
    string? LastDiagnostic);
```

Create a bounded `Channel<RegionCorpusWriteRequest>` with `SingleReader=true`, `FullMode=Wait`, and configurable positive capacity defaulting to `16`. `TryRecord` validates a non-disposed BGRA snapshot, clones its exact pixel memory into a new array, then calls `TryWrite`; false increments dropped and returns immediately. The worker calls the sink, updates written/deduplicated/failed metrics, decrements pending in `finally`, and sanitizes diagnostics to exception type plus message. `DisposeAsync` atomically stops acceptance, completes the channel, and awaits the worker so accepted samples drain.

- [ ] **Step 4: Run foundation tests and verify GREEN**

Expected: queue rejection, metrics, failure isolation, and drain behavior pass without timing sleeps.

- [ ] **Step 5: Commit Task 3**

```powershell
git add -- src/AstralTFT.Capture/Replay/BoundedRegionCorpusRecorder.cs tests/AstralTFT.Foundation.Tests/Program.cs
git commit -m "Add bounded replay corpus recorder"
```

---

### Task 4: Opt-in shop-slot capture integration

**Files:**
- Create: `src/AstralTFT.Capture/Replay/RegionCorpusConfiguration.cs`
- Create: `src/AstralTFT.Capture/Replay/ShopSlotCorpusCapture.cs`
- Modify: `src/AstralTFT.App/MainWindow.xaml.cs`
- Modify: `docs/CAPTURE_PROTOTYPE_PLAN.md`
- Modify: `tests/AstralTFT.Foundation.Tests/Program.cs`

**Interfaces:**
- Consumes: `BoundedRegionCorpusRecorder`, `ShopSlotRecognizer.ProjectSlots`, `CpuBgraRegionSnapshotFactory`, and live `CapturedFrame`.
- Produces: pure environment configuration parsing and a reusable five-slot submission helper.

- [ ] **Step 1: Write failing configuration and slot-copy tests**

Register:

```csharp
("Corpus configuration requires an absolute directory", CorpusConfigurationRequiresAbsolutePath),
("Shop corpus capture submits packed slot snapshots", ShopCorpusCaptureSubmitsPackedSlots),
```

Assert blank configuration is disabled with no error, a relative path is disabled with a diagnostic, and an absolute temporary path is enabled. For a padded synthetic 1152×239 BGRA buffer, run `ShopSlotCorpusCapture.TryRecordChangedShop`; use a collecting sink/recorder, drain, then assert five `shop-slot-N` requests in order, packed stride, original sequence/timestamp, and exact first/last copied pixels.

- [ ] **Step 2: Run foundation tests and verify RED**

Expected: compilation fails because configuration and slot capture types do not exist.

- [ ] **Step 3: Implement pure configuration parsing**

Define `RegionCorpusConfiguration` with `Enabled`, `DirectoryPath`, and `Diagnostic`. `FromEnvironmentValue(string?)` trims input, returns disabled for blank, rejects non-absolute or invalid paths without creating directories, and returns `Path.GetFullPath` for an enabled value.

- [ ] **Step 4: Implement reusable shop-slot submission**

`ShopSlotCorpusCapture.TryRecordChangedShop` projects the five slot ROIs, uses `CpuBgraRegionSnapshotFactory.Create(frame, slot)`, casts to `Bgra32RegionSnapshot`, submits with `LiveCapture`, and disposes each temporary snapshot. Return accepted count; isolate individual slot copy/enqueue failures without changing recognition state.

- [ ] **Step 5: Wire the recorder into `MainWindow`**

At capture attachment, parse `Environment.GetEnvironmentVariable("ASTRALTFT_CORPUS_DIRECTORY")`. If enabled, construct `RegionCorpusStore` and `BoundedRegionCorpusRecorder`; if construction fails, keep capture running and surface a sanitized footer diagnostic. Pass the recorder into `ConsumeFramesAsync`.

After `_shopHudConfirmed && hud.IsVisible && change.IsMeaningful`, submit the five slot samples exactly once for that frame before recognition formatting. Store the recorder in a field, detach it before stopping capture, wait for the frame consumer, then `await DisposeAsync()` to drain it. Do not enable recording for hold-only or unconfirmed frames.

- [ ] **Step 6: Document the developer switch**

Add a “Developer replay corpus” section to `docs/CAPTURE_PROTOTYPE_PLAN.md` with this PowerShell example:

```powershell
$env:ASTRALTFT_CORPUS_DIRECTORY = 'D:\AstralTFT-Corpus\set18-shop'
dotnet run --project .\src\AstralTFT.App\AstralTFT.App.csproj -c Release
```

State that capture is local, shop-slot-only, opt-in, and may add developer-mode copy/I/O overhead.

- [ ] **Step 7: Run foundation tests and verify GREEN**

Expected: configuration and exact slot-copy tests pass; all prior tests remain green.

- [ ] **Step 8: Commit Task 4**

```powershell
git add -- src/AstralTFT.Capture/Replay/RegionCorpusConfiguration.cs src/AstralTFT.Capture/Replay/ShopSlotCorpusCapture.cs src/AstralTFT.App/MainWindow.xaml.cs docs/CAPTURE_PROTOTYPE_PLAN.md tests/AstralTFT.Foundation.Tests/Program.cs
git commit -m "Wire opt-in shop replay corpus capture"
```

---

### Task 5: Final verification, review, push, and CI

**Files:**
- Verify all files committed by Tasks 1–4.
- No new production file is expected.

**Interfaces:**
- Consumes: the complete implementation and approved design.
- Produces: review evidence, pushed commits, and a successful GitHub Windows CI run.

- [ ] **Step 1: Run whitespace and repository-scope checks**

```powershell
git diff --check HEAD~4..HEAD
git status --short --branch
```

Expected: only the pre-existing untracked helper scripts remain; no implementation changes are unstaged.

- [ ] **Step 2: Run the exact local CI build and tests**

```powershell
dotnet restore .\AstralTFT.slnx
dotnet build .\AstralTFT.slnx -c Release --no-restore
dotnet run --project .\tests\AstralTFT.State.Tests\AstralTFT.State.Tests.csproj -c Release --no-build
dotnet run --project .\tests\AstralTFT.Foundation.Tests\AstralTFT.Foundation.Tests.csproj -c Release --no-build
dotnet publish .\src\AstralTFT.App\AstralTFT.App.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\AstralTFT-win-x64
```

Expected: build exit `0`, state and foundation suites report zero failures, and publish exit `0`.

- [ ] **Step 3: Request read-only code review**

Review the design and full implementation range for capture-path blocking, ownership/disposal errors, unsafe paths, unbounded allocations, replay corruption handling, policy violations, and missing mutation-resistant tests. Resolve every Critical and Important issue with a new red/green cycle.

- [ ] **Step 4: Push and monitor Windows CI**

```powershell
git push origin main
```

Query the GitHub Actions run for the pushed head SHA and wait until `status=completed`. Completion requires `conclusion=success`; inspect failed logs and repair otherwise.
