# Capture Prototype Plan

## Gate 0 — window discovery

Implemented in scaffold:

- Enumerate visible top-level windows.
- Exclude minimized windows.
- Score title/process patterns instead of binding to one exact executable name.
- Keep this behind `IGameClientAdapter` so the dedicated TFT client can receive its own adapter later.

## Gate 1 — Windows Graphics Capture

Implementation target:

1. Create a `GraphicsCaptureItem` from TFT's HWND via `IGraphicsCaptureItemInterop.CreateForWindow`.
2. Use a free-threaded Direct3D11 capture frame pool so capture callbacks do not require the WPF UI dispatcher.
3. Keep callbacks extremely short: retain/copy only the GPU resource reference and enqueue newest work.
4. Detect resize/client-layout changes and recreate the frame pool safely.
5. Packaged build: ask once for Windows borderless-capture permission; gracefully accept the normal capture border if permission is denied. Portable/unpackaged builds must not assume the manifest-gated borderless capability is available.
6. Do not attempt to capture minimized TFT because Windows Graphics Capture does not provide useful minimized content.

## Gate 2 — overhead benchmark

Before computer vision is enabled, measure capture-only overhead on the target PC:

- TFT FPS / frame-time comparison with app closed vs attached.
- companion CPU/GPU/RAM.
- capture callback rate.
- dropped/stale frames.

If capture alone causes measurable regression, stop and fix it before adding recognizers.

## Gate 3 — change detection

First recognizer-like workload is not OCR or ML. It is low-cost region fingerprinting.

Set 18 initial regions include:

- shop slots, including Wisp-aware far-right slot
- item bench
- augment modal
- gold/level/stage
- bench

Board recognition comes later because temporal purchase/movement tracking can reduce its workload.

## Windows preflight discovery

Before hard-coding any Unreal-era process name, run `scripts-doctor.ps1` on the target Windows machine while TFT is in a match. The report captures process/window titles and client dimensions without reading game memory. The first benchmark should use this to validate `TftWindowLocator` against the actual Set 18 executable and again when Riot ships the dedicated TFT client.

## Gate 2.5 — CPU fallback fingerprint

A package-free `GridLumaRegionChangeDetector` now exists for diagnostics/tests. It samples a small luminance grid inside each ROI and is deliberately not the final production path. The production benchmark should compare it against a GPU ROI fingerprint and keep whichever path has lower total cost on the target PC.

`ChangedRegionSelector` adds per-region recheck intervals and priority ordering so augment/shop transitions can be processed before background verification.

## Frame lifetime and recognition back-pressure

The capture API uses a single-consumer `IAsyncEnumerable<CapturedFrame>` rather than a public frame event. Each `CapturedFrame` is an explicit resource lease and is disposed immediately after changed ROIs are copied/leased into detector-safe snapshots.

Recognition backlog is bounded in two places:

1. `ChangedRegionSelector` limits which changed ROIs are eligible per frame.
2. `CoalescingRecognitionQueue` keeps only the newest pending snapshot for each detector/region pair and refuses lower-priority work rather than evicting a more important augment/shop decision.

`DetectorHealthTracker` circuit-breaks a failing recognizer independently, while `RecognitionResultSequenceGate` prevents slow old worker results from overwriting newer state. This is intentionally designed before WGC interop so native frame lifetime and back-pressure cannot become accidental implementation details later.
