# State Engine Design

## Core rule

The accepted `GameState` is not a raw frame interpretation. It is the output of observation fusion plus deterministic event reduction.

```text
capture -> detector observations -> fusion -> accepted events -> reducer -> immutable state
```

## Authoritative mutation

`GameStateReducer` is the only component that should mutate accepted state. It consumes typed `GameEvent` instances and produces a new immutable `GameState`.

Semantic/inferred events such as `UnitPurchasedEvent` and `UnitAcquiredEvent` are retained for analysis, while accepted snapshot events (`RosterSnapshotAcceptedEvent`, `ShopSnapshotAcceptedEvent`, etc.) carry the evidence needed to reconstruct authoritative state.

## Timeline

`GameTimeline` assigns monotonically increasing sequence numbers and supports deterministic replay. Persistent storage should use this sequence as the canonical event order, not timestamps alone.

Stale detector results should be dropped before they reach the state actor whenever a newer observation for the same region has already been accepted.

## Acquisition inference

Early PvE/orb drops are inferred from owned-copy deltas rather than requiring loot-animation recognition:

1. Compare accepted previous/current copy counts.
2. Subtract confirmed purchase evidence.
3. Attribute unexplained positive deltas to carousel/PvE when the round context supports it.
4. Reduce confidence when there is a large observation gap.
5. Do not invent an acquisition source when context is ambiguous.

This supports Stage 1 high-cost drops without adding a heavy animation detector.

## Temporal fusion implementation

`TemporalObservationFusion<T>` enforces the initial stability rules:

- confirmed observations can replace state immediately;
- probable changes require a repeated matching observation inside the confirmation window;
- unknown/low-confidence frames never erase a stable accepted value;
- stale observations are rejected;
- same-value observations can refresh confidence/timestamps without generating a semantic state change.

This is intentionally detector-agnostic so shop OCR, item matching and board tracking can share the same acceptance behavior.
