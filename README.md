# AstralTFT (working title)

A performance-first Windows TFT companion/analysis project.

## Project goals

- Very low impact on TFT performance.
- Automatic TFT-window detection and efficient state recognition.
- Event-driven game-state tracking instead of repeatedly re-reading everything.
- High-confidence recognition with explicit unknown/probable states rather than silent guessing.
- Local-first storage, optional cloud backup later.
- Purple/astral visual identity with lightweight adaptive theming.
- Modular architecture so Riot/client/patch changes can be fixed without rewriting the app.
- Current-game state capture and post-game coaching are separated from the live presentation layer.

## Current architecture decision

- Runtime: .NET 10 LTS (`net10.0-windows10.0.19041.0`).
- UI candidate: WPF/Win32 with selective Windows App SDK APIs. Final UI framework remains benchmark-gated.
- Capture: Windows Graphics Capture against the TFT HWND.
- ML: ONNX Runtime/Windows ML only when cheap recognizers are insufficient.
- Storage: SQLite via `Microsoft.Data.Sqlite`.
- State: immutable snapshots + event timeline + observation fusion.

## Repository layout

- `src/AstralTFT.Core` — shared domain models and contracts.
- `src/AstralTFT.State` — observation fusion, state machine, ownership/star tracking, event timeline.
- `src/AstralTFT.Capture` — window discovery and frame-source contracts; Windows capture implementation lives here.
- `src/AstralTFT.Analysis` — board/comp/item/augment scoring and personalization.
- `src/AstralTFT.Infrastructure` — SQLite, settings, logging, diagnostics, updater infrastructure.
- `src/AstralTFT.App` — companion UI, overlay, themes.
- `docs` — architecture, policy boundary, data-source and performance specifications.

## Important development rule

Recognition and state capture are deliberately decoupled from live recommendations. Riot's current TFT policy explicitly disallows dynamic real-time prescriptions based on the player's current game state. The project should preserve the ability to perform deep post-game analysis while keeping the live product within the approved-use boundary.

## Build status

This environment does not currently contain the .NET SDK, so the scaffold has not yet been compiled here. The files are structured to be opened/built on Windows with Visual Studio 2026 / .NET 10 SDK. The state/event interfaces are being kept package-free so their logic can be validated independently before Windows-only capture dependencies are pinned.

## Current checkpoint

Implemented architecture-level foundations now include:

- deterministic immutable game-state reducer and timeline
- single-writer async `GameStateActor`
- temporal observation fusion with confirmed/probable/unknown handling
- owned-copy/star tracking and 4/5-cost 3-star pursuit model
- PvE/orb acquisition inference
- Set 18 Wisp-aware shop model
- normalized layout-to-pixel ROI projection
- lightweight grid-luma change detector for benchmark/fallback use
- priority/recheck-aware changed-region selector
- adaptive Eco/Balanced/Responsive performance governor
- bounded performance telemetry
- source-quality, patch-blend, robust multi-source metric ensemble and trend detector
- feature gating/Safe Mode foundation
- separate active augment-offer state for later retrospective choice/reroll analysis
- Windows preflight script that exports machine-readable JSON

The Windows Graphics Capture implementation itself remains benchmark-gated until the first Windows/.NET 10 compile pass; the repository deliberately does not pretend a Linux-only scaffold has validated WinRT/D3D interop.

## Windows CI

The repository includes `.github/workflows/windows-ci.yml`. GitHub Actions builds the solution on a real Windows runner, runs the state/foundation self-tests, publishes a `win-x64` diagnostic build, and uploads it as an Actions artifact. See `docs/CI.md` for the verification flow and the separate real-hardware benchmark gate.
