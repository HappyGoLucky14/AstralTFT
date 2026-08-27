# Offline Replay Corpus Design

Status: approved for implementation on 2026-08-28.

## Goal

Give AstralTFT a deterministic, local-only corpus of changed shop-slot pixels so recognition can be developed and regression-tested without repeatedly requiring a live 40-minute TFT match.

This slice builds recording and replay infrastructure. Champion identity classification and template acquisition are separate follow-on slices that consume this corpus.

## Scope

The first version will:

- Record only explicitly selected TFT regions, initially the five projected shop slots.
- Be disabled unless a developer supplies a corpus directory through an explicit environment setting.
- Copy accepted pixels quickly, enqueue them without waiting, and perform hashing and file I/O on one background worker.
- Drop new samples when its bounded queue is full instead of slowing capture.
- Store canonical packed BGRA32 blobs addressed by SHA-256, with a versioned JSON Lines observation log.
- Deduplicate identical pixel blobs while retaining every accepted observation's sequence and timestamp.
- Replay observations deterministically as `Bgra32RegionSnapshot` instances in log order.
- Reject malformed metadata, invalid geometry, oversized samples, missing blobs, and hash mismatches.
- Keep all corpus data outside the repository and outside the normal application state store.

The first version will not:

- Record the desktop, other windows, process memory, network traffic, or opponent state.
- Encode PNG or video on the capture path.
- Add champion labels, template matching, ML inference, or automatic uploads.
- Promise crash recovery for a partially written final JSONL line beyond ignoring that incomplete tail with a diagnostic count.

## Architecture

### Canonical sample

Every stored sample is packed BGRA32 with `Stride == Width * 4`. Padding from a capture source is removed before enqueueing. Geometry is bounded to positive dimensions no larger than 4096 by 4096, and the canonical byte length must equal `Stride * Height` and remain at or below 64 MiB.

The content identifier is lowercase SHA-256 over this canonical byte sequence:

1. ASCII marker `AstralTFT-BGRA32-v1` followed by a zero byte.
2. Width, height, and stride as three little-endian signed 32-bit integers.
3. The packed pixel bytes.

Including geometry prevents the same byte sequence from being interpreted using a different image shape.

### Corpus layout

```text
<corpus-root>/
  corpus.json
  observations.jsonl
  blobs/
    <sha256>.bgra
```

`corpus.json` is created once and contains schema version `1`, pixel format `Bgra32`, creation time, and the creating AstralTFT version. A mismatched schema or pixel format is rejected.

Each `observations.jsonl` entry contains:

- schema version
- content hash
- region ID
- frame sequence
- UTC capture timestamp
- width, height, and stride
- source kind (`LiveCapture` or `ImportedFrame`)

Blob filenames are derived only from validated lowercase hexadecimal hashes. Region IDs and other metadata never become paths.

### Atomicity and recovery

New blobs are written to a generated temporary file in the corpus root, flushed, then atomically moved into `blobs`. If another observation already wrote the same blob, the temporary file is discarded and the existing validated blob is reused.

Observation lines are serialized as one compact JSON object plus a newline and flushed after each accepted sample. Replay accepts a missing trailing newline, but if the final line is invalid JSON it is counted as an ignored incomplete tail. Invalid lines before the final non-empty line fail the replay so silent mid-log corruption cannot reorder or hide data.

### Recording path

`BoundedRegionCorpusRecorder` owns a bounded `Channel` with capacity 16 and a single background writer. `TryRecord`:

1. Validates the source snapshot.
2. Copies the exact canonical bytes into an owned array.
3. Uses non-blocking `TryWrite` against a channel configured with wait semantics, so a full channel returns `false` rather than waiting.
4. Updates accepted or dropped counters.

The worker computes SHA-256, persists the blob and observation, and records written, deduplicated, and failed counters. Disposal completes the channel and drains accepted work before returning. An absent recorder means the production path performs no corpus checks, copies, hashes, or I/O.

### Capture integration

The application reads `ASTRALTFT_CORPUS_DIRECTORY` once when capture starts. Blank or missing means disabled. When enabled, it creates one recorder for that directory.

After the shop HUD is confirmed and the shop band changes meaningfully, the app projects the five existing shop-slot ROIs and submits one packed sample per slot. Samples retain the WGC frame sequence and capture timestamp. Recording never authorizes recognition and never changes game state.

The UI footer may mention that developer corpus recording is active, but recording failures remain diagnostic information and must not stop capture or recognition.

### Replay boundary

`RegionCorpusReader.ReadAsync` validates the header and each observation, then yields owned `Bgra32RegionSnapshot` objects in file order. Callers must dispose each snapshot. Future champion detectors will consume these snapshots through the existing `IRegionObservationDetector` contract, keeping live and offline behavior aligned.

## Performance and resource policy

- Normal mode: zero corpus allocation and I/O because no recorder exists.
- Developer mode: only meaningful confirmed-shop changes are sampled.
- Queue capacity is fixed at 16 in this slice and never grows dynamically.
- Enqueue never waits for disk or hashing.
- One writer prevents unbounded concurrent I/O.
- Identical pixel content is stored once.
- Recorder metrics expose accepted, dropped, written, deduplicated, failed, and pending counts.

## Privacy and Riot-policy compliance

The corpus contains pixels already visible in the user's TFT window and only from selected AstralTFT ROIs. It does not inspect game memory, inject code, automate input, infer unavailable Wisp contents, scan opponent boards, or upload data. Developer capture is explicit and local-only.

## Error handling

- Invalid configuration disables recording and surfaces a diagnostic message; it does not terminate capture.
- A rejected enqueue increments `Dropped` and releases its owned bytes immediately.
- A writer failure increments `Failed`, stores the latest sanitized diagnostic, and continues with later samples when safe.
- Replay fails fast for header mismatch, unsafe geometry, non-final corrupt log entries, missing blobs, unexpected blob length, or content-hash mismatch.
- Cancellation stops replay enumeration. Recorder disposal drains already accepted work without accepting new work.

## Test strategy

Foundation self-tests will cover:

- Canonical hash determinism and geometry sensitivity.
- Exact pixel, geometry, sequence, region, and timestamp round-trip.
- Blob deduplication with multiple observation records.
- Packed copying from padded BGRA input.
- Queue-full rejection without blocking and accurate metrics.
- Drain-on-dispose behavior.
- Missing, oversized, malformed, and hash-mismatched samples.
- Ignoring only an incomplete final JSONL line.
- Deterministic observation ordering.
- Configuration disabled/enabled parsing without touching disk when disabled.

The full Release build, state self-tests, foundation self-tests, framework-dependent Windows publish, code review, push, and GitHub Windows CI remain the completion gate.
