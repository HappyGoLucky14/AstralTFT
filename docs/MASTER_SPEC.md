# AstralTFT v1 Master Specification

Status: architecture baseline / pre-prototype

## 1. Product principles

1. **Performance before feature count.** TFT FPS and input responsiveness are the hard ceiling.
2. **Accuracy before confidence theater.** Unknown is preferable to a confident wrong state.
3. **Event-driven, not brute-force.** Recognition work happens only when a relevant region changes.
4. **Stateful recognition.** Purchase/move/acquisition history reduces the need to repeatedly classify 3D units.
5. **Modular maintenance.** Capture, layout, vision, state, analysis, data sources, UI, and updates are replaceable modules.
6. **Local-first.** Reconstructed states are stored locally; screenshots are not retained except failed-recognition diagnostics when enabled.
7. **Adaptive, clean UI.** Purple/astral base identity; current arena/tactician may subtly influence palette and panel contrast.
8. **Policy-aware presentation.** Capture/analysis capabilities are technically separated from the live UI so approved live behavior can be enforced independently.

## 2. Target machine profile

Primary optimization target:

- Windows 11
- Ryzen 7 1700
- RTX 3060 Ti
- 48 GB RAM
- 1920x1080 TFT, borderless
- Three monitors
- Ranked first; Double Up may be supported later

The design should use spare RAM to reduce CPU/GPU recomputation where that is measurably beneficial.

## 3. Runtime behavior

- Default launch mode: automatically launch when TFT is detected/opened.
- Alternative setting: launch with Windows.
- Fully close when TFT closes (latest preference supersedes tray-sleep behavior).
- Detect TFT window by process/window characteristics through a versioned client adapter.
- Capture only TFT's window surface, not the entire desktop.
- Prefer borderless Windows Graphics Capture when user grants the required OS permission.
- Automatically pause/reduce recognition when minimized, obscured, or unchanged.

## 4. State recognition scope

The state model should be capable of representing:

- Shop champions
- Board champions and star levels
- Bench champions and star levels
- Items and components
- Item bench
- Augments
- Traits
- Gold
- Level and XP
- HP
- Stage
- Current owned-copy counts
- Unit acquisition events
- PvE/orb unit drops, including unusually high-cost Stage 1 drops
- Current equipped arena/tactician (for theming/cosmetics)

Opponent-board auto-scanning is explicitly excluded from the approved live feature set under Riot's current TFT policy.

## 5. Observation model

Every observation includes:

- value
- confidence [0,1]
- source detector
- timestamp
- region/layout profile
- optional supporting evidence

Fusion status:

- `Confirmed`
- `Probable`
- `Unknown`

A single low-confidence frame must not overwrite a stable confirmed state.

## 6. Event-driven state engine

Examples:

- `ShopChanged`
- `UnitPurchased`
- `UnitSold`
- `UnitAcquiredFromPve`
- `UnitMovedBoardBench`
- `StarLevelChanged`
- `ItemAttached`
- `ItemDetachedOrMoved`
- `AugmentSelectionPresented`
- `AugmentSelected`
- `GoldChanged`
- `LevelChanged`
- `StageChanged`

This is the durable history used by post-game analysis and personalization.

## 7. Early PvE/orb unit drops

The engine must explicitly model early unit drops because an early 2/3-cost can alter tempo and strongest-board choices.

Preferred detection route:

1. State difference indicates a new owned unit.
2. No matching purchase event exists.
3. Timing occurs in PvE/loot context.
4. Mark acquisition source as `PveOrLoot` with confidence.
5. Re-evaluate board-strength potential, trait access, temporary item-holder value, sell/econ value, and bench pressure.

The system should not depend on recognizing the loot animation itself.

## 8. Unit-copy/star logic

Track ownership in base-copy equivalents:

- 1-star = 1 copy
- 2-star = 3 copies
- 3-star = 9 copies

For 4/5-costs after 2-star, keep a possible 3-star pursuit state instead of automatically marking the unit complete. The feasibility model should account for known pool rules and, where permissible/reliably available, remaining-copy evidence. In the live approved product, opponent-board scanning cannot be used to generate this evidence.

Possible pursuit states:

- `Closed`
- `Open`
- `LowProbability`
- `HighProbability`

## 9. Performance governor

Primary behavior:

- Do not run all detectors at a fixed FPS.
- Cheaply fingerprint/calculate change for regions of interest.
- Run only the detector for a region that changed.
- Cache current-set assets and embeddings in RAM.
- Use GPU acceleration only where benchmarked faster/cheaper than CPU.
- Back off automatically if companion CPU/GPU pressure becomes measurable.

Suggested initial targets (to benchmark, not promises):

- Idle with TFT absent: effectively 0% CPU.
- Stable planning board: <1-2% average CPU target.
- Active recognition: short low-single-digit CPU bursts where possible.
- RAM: 250 MB-1 GB acceptable if caching lowers CPU/GPU cost.
- UI rendering: state-change driven; no unnecessary 60 Hz data recomposition.
- No perceptible TFT FPS degradation.

## 10. UI model

Two surfaces share one state/analysis backend:

### Overlay

- Optional and intentionally minimal; the companion window is the primary live information surface.
- No round coaching/prompt cards by default.
- Reserve overlay space for essential status only if later testing proves it useful.
- Automatically repositions around occupied TFT UI when enabled.
- One-key hide-all hotkey.
- Lightweight adaptive background/contrast; expensive blur optional only.

### Companion window

- Primary live information and round-review surface; intended to sit on a second/third monitor.
- User can drag it to any monitor; position is remembered.
- Live updates are allowed architecturally but rendering should be event-driven/throttled if benchmarks show cost.
- Round analysis must be targeted, concrete and comp-playstyle-specific; suppress generic filler.
- Primary tabs around: Live, Meta/Comps, Augments/Items, History, Profile, Settings/Diagnostics.
- Top 3 lines in analysis contexts; more lines expandable.

## 11. Theme/cosmetics

Base design: clean Riot/TFT-inspired interface with a purple/astral identity.

Theme modes:

- Personal — fixed purple/astral
- Adaptive — purple/astral + current arena/tactician palette (default)
- Cosmetic Match — stronger cosmetic-derived influence
- Manual

Current arena/tactician should be identified once per match where practical. Palette extraction should be cached and blended with a lightweight background luminance sample. No continuous expensive blur requirement.

Owned-cosmetic inventory support is research-gated: prefer supported Riot/local-client sources; otherwise allow manual collection import/selection.

## 12. Analysis principles

Post-game analysis engine should eventually evaluate:

- strongest-board progression
- item slam vs hold value
- component flexibility
- comp/line fit
- augment fit and reroll value
- economy/level timing
- transition cost
- board upgrades
- personal historical performance
- rank/LP context
- patch/meta trend strength
- condition-dependent comps (hero augments, emblems, artifacts)

Optimization objective:

- dynamic expected value with a Top-4/placement bias
- allow higher-ceiling paths when state supports it
- actual position can outweigh generic tier-list strength
- personal profile weight should be learned rather than manually overfit

## 13. Meta data behavior

- Use several sources, weighted by sample quality, freshness, rank filters, and reliability.
- Global baseline with EUW-specific adjustments when meaningful.
- Blend rank brackets based on the user's current rank/LP and sample size, with reliable high-Elo data weighted more.
- Recency weighting within a patch should be moderate.
- Trend detector should aggressively look for rising lines but require confidence/sample safeguards.
- New patch: begin transition within hours; initially blend prior-patch priors, then decay them quickly as reliable new-patch data accumulates.
- Retain historical patches/sets in an archived form.
- Expose subtle freshness/confidence metadata.

## 14. Persistence/privacy

- Persist reconstructed events/states indefinitely unless deleted by user.
- Do not persist normal capture frames.
- Optionally save only failed/ambiguous recognition frames for debugging.
- Optional cloud backup later: reconstructed state only by default, not screenshots.
- Anonymous crash + performance diagnostics allowed, with opt-out.
- Rotate logs by size.
- Support deleting individual games, date ranges, patches/sets, or all history.

## 15. Updates/recovery

- Small application fixes may auto-install; larger updates ask first.
- Meta/database updates are silent with subtle status.
- Settings/history must survive updates.
- Installer + portable builds.
- Local-first v1; architecture leaves room for optional accounts/cloud later.
- Safe Mode must disable risky/broken live modules after a TFT patch while preserving history/meta/basic app functionality.

## 16. Development sequencing

1. Domain/state schema
2. Observation fusion
3. Event timeline + SQLite persistence
4. Window discovery + capture benchmark
5. ROI/change detector
6. Shop/item/augment recognition proof
7. Board/bench temporal tracking
8. Client/layout profile system
9. Performance governor
10. Companion diagnostics UI
11. Static/meta updater
12. Post-game analysis engine
13. Personal profile/rank+LP tracking
14. Adaptive theme/cosmetics
15. Production overlay
16. Installer/updater/safe-mode hardening

A recommendation system should not be built before the state engine is accurate enough to trust.

## 17. Round review and comp-specific analysis addendum

- Round prompts/coaching belong in the companion window by default, not the overlay.
- Generic advice is suppressed. A review must be position-specific, evidence-backed, and tied to the current/likely comp playstyle.
- Comp direction remains probabilistic early; do not hard-lock a line because one unit/item appears.
- `CompPlaystyleProfile` must support reroll, tempo, Fast 8/9, vertical, flex, capped-board, streak and other archetypes.
- Different archetypes receive different interpretations of identical HP/gold/level states.
- Rich state-driven prescriptions remain presentation-policy gated under Riot's current third-party rules; detailed retrospective/post-game analysis remains the primary unrestricted coaching surface.

## 18. Set 18 Wisp handling addendum

Set 18's Wisp mechanic invalidates a five-champion-only shop model. The rightmost visible shop entry may be a Wisp, and the hidden champion should remain unknown until it is actually revealed. `ShopEntry` therefore remains a typed Champion/Wisp/Unknown value. Wisp purchases are semantic game events because they can alter economy, items, combat strength, shop behavior, or champion acquisition.

## 19. Capture packaging caveat

- Programmatic HWND capture uses Windows Graphics Capture/Win32 interop.
- Microsoft requires the `graphicsCaptureWithoutBorder` package-manifest capability plus explicit user consent before `IsBorderRequired=false` can be honored.
- Therefore the normal packaged installer/MSIX path should be the preferred build when borderless capture is desired.
- The portable/unpackaged build remains supported, but may have to accept the system capture border if Windows does not grant borderless capability without package identity.
- Capture must continue to work correctly if borderless permission is denied; this is visual polish, not a functional dependency.
