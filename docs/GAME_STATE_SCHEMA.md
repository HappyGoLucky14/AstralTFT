# Game State Schema

## Design goals

- Compact enough to store thousands of games.
- Immutable snapshots for safe UI consumption.
- Event history is the canonical replayable record.
- Every uncertain field carries confidence/provenance instead of silently becoming fact.

## Core state

```text
GameState
  GameId
  Patch
  Set
  QueueType
  Timestamp
  Stage
  Player
    HP
    Gold
    Level
    XP
    RankSnapshot
  Shop[5]
  Board[]
  Bench[]
  ItemBench[]
  Augments[]
  ActiveAugmentOffer?
  OpeningEncounterId?
  Traits[]
  UnitOwnership{}
  Cosmetics
  LayoutProfile
  RecognitionHealth
```

## Unit instance

```text
UnitInstance
  InstanceId
  ChampionId
  StarLevel
  BoardOrBenchSlot
  Items[]
  Confidence
  FirstSeenAt
  AcquisitionSource
```

## Acquisition source

- ShopPurchase
- PveOrLoot
- Carousel
- Duplicator
- SpecialMechanic
- Unknown

## Observation provenance

```text
Observation<T>
  Value
  Confidence
  Source
  ObservedAt
  RegionId
  EvidenceHash
```

## Recommended confidence rules

- >= 0.97: can confirm immediately for stable/icon-based observations.
- 0.85-0.97: confirm only with temporal/state consistency.
- 0.60-0.85: probable; do not overwrite a stable contradictory state without supporting evidence.
- < 0.60: unknown for state mutation purposes.

Thresholds are detector-specific and must be benchmarked; these are bootstrap defaults only.

## Timeline storage

Prefer events plus sparse checkpoints rather than full snapshots every frame.

- Event records: small JSON/binary payloads.
- Checkpoint: every major stage or N accepted events.
- Debug frame: only on low-confidence/failure when enabled.

## Active augment offer

```text
AugmentOfferState
  OfferIndex
  OfferedAugmentIds[]
  RerollsRemaining
  ObservedAt
  Confidence
```

The active offer is separate from selected `Augments[]`. This is required for accurate retrospective analysis of augment choices and reroll usage. It is cleared when an augment is accepted.
