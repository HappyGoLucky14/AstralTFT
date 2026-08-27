# Performance Budget

The budget is measured against **impact on TFT**, not just the companion's own process metrics.

## Hard priorities

1. Avoid persistent CPU saturation on Ryzen 7 1700.
2. Do not create meaningful frame-time spikes in TFT.
3. Avoid GPU contention with TFT on RTX 3060 Ti.
4. Prefer RAM caching when it removes repeated decode/inference work.

## Scheduling rules

- No fixed full-screen CV loop.
- Capture may be high-frequency, but expensive recognition is change-triggered.
- Hash/fingerprint small ROIs first.
- Ignore unchanged regions.
- Bound each detector's queue to newest work.
- Coalesce rapid shop transitions.
- Increase detector frequency briefly when a known modal screen appears (e.g., augment selection), then back off.

## Initial benchmark gates

A feature should not ship enabled by default if it causes either:

- a repeatable visible TFT frame-time regression, or
- sustained companion CPU/GPU usage without corresponding information benefit.

## Cache candidates

- Current-set champion portraits
- Item/component icons
- Augment icons
- Precomputed perceptual hashes
- Small feature embeddings
- Layout templates
- Current-patch normalized meta records

## Diagnostics to collect locally

- capture callback rate
- changed-ROI rate
- detector invocation count
- detector p50/p95 latency
- state-fusion latency
- dropped/stale work count
- process working set
- process CPU
- optional GPU engine utilization where measurable

The diagnostics view should be off the hot path and should not itself cause a performance issue.
