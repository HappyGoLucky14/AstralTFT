# Architecture

## High-level data flow

```text
TFT HWND
  -> Capture Source
  -> ROI Extractor / Change Detector
  -> Detector(s)
  -> Observation Fusion
  -> Game State Reducer
  -> Event Timeline
  -> SQLite
  -> Analysis Engine
  -> Overlay / Companion / Post-game Review
```

## Dependency rule

Lower layers never depend on UI or recommendation logic.

```text
Core <- Capture
Core <- State
Core <- Meta
Core + State + Meta <- Analysis
Core + State + Meta <- Infrastructure
Core + State + Capture + Meta + Analysis + Infrastructure <- App
```

## Why this structure

- A new TFT client should require a new `IGameClientAdapter` and layout profiles, not a new analysis engine.
- A broken augment detector should be replaceable without touching unit ownership logic.
- The same state timeline should power diagnostics, post-game review, and future ML personalization.
- Live-policy restrictions can be enforced in the presentation layer without deleting useful post-game capabilities from the engine.

## Key contracts

- `IGameClientAdapter`: locate/validate the current TFT game window and version/layout family.
- `IFrameSource`: single-consumer async stream of explicitly-owned GPU/CPU frame leases; callers dispose each frame after ROI extraction.
- `IRegionChangeDetector`: fingerprint ROIs and signal meaningful changes.
- `IRegionObservationDetector`: one composite detector per logical UI region; produces confidence-bearing recognition batches.
- `IObservationFusion`: combine noisy observations with prior state.
- `IGameStateReducer`: apply accepted events to an immutable `GameState`.
- `IEventStore`: persist/replay game events.
- `IPerformanceGovernor`: dynamically set detector budgets.
- `IDataSourceAdapter`: normalize patch/meta/static sources into internal records.
- `IAnalysisModule`: score a state/timeline without directly owning UI behavior.

## Threading model

- Native capture callback: minimum work; copy/lease the newest GPU texture into a bounded frame source and release the WinRT capture frame immediately.
- Frame consumer: owns one `CapturedFrame` lease, runs cheap ROI fingerprints, snapshots only changed regions, then disposes the frame.
- Detector workers: bounded queues; never allow runaway backlog.
- State actor: single logical writer to GameState, preventing races.
- Persistence writer: async/batched.
- UI: consumes immutable snapshots/events.

If the detector queue falls behind, drop stale work and process the newest relevant frame. TFT analysis does not benefit from recognizing a shop that disappeared 800 ms ago.

## Failure containment

Each module reports health:

- Healthy
- Degraded
- Disabled
- IncompatibleLayout

Safe Mode can disable detectors/overlay independently while preserving persistence and history.

## Platform targeting

- Core, State, Meta, Analysis and Infrastructure target plain `net10.0`; they must stay free of Windows-only UI/capture APIs.
- Capture and App target `net10.0-windows10.0.19041.0`.
- This keeps the reasoning/state/meta code independently testable and prevents Windows interop from leaking into the domain model.
