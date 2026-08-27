# Windows capture benchmark gate

This is the first runtime gate before any OCR/ML recognizer is enabled.

## What the diagnostic build does

- locates the TFT match HWND
- captures only that window with Windows.Graphics.Capture
- creates a BGRA-capable D3D11 hardware device
- uses a free-threaded WGC frame pool
- throttles CPU readback to 10 accepted frames/second
- reuses the staging texture and rents CPU byte arrays from ArrayPool
- keeps newest-frame semantics instead of accumulating stale captures
- pauses capture when TFT is minimized
- survives transient TFT-window handoffs before deciding the match window closed
- exports objective JSON metrics automatically

It does **not** read TFT process memory, inject into TFT, use OCR, use ML, or scan opponent boards.

## Success criteria for the first machine test

These are gates rather than promises:

1. Correct TFT match HWND is selected reliably.
2. Capture begins without a picker or desktop-wide capture.
3. No repeated WGC/D3D errors over a normal game segment.
4. GPU→CPU readback p95 is comfortably below the 100 ms 10-FPS budget.
5. AstralTFT CPU use remains low enough to be unnoticeable while playing.
6. Managed heap remains stable instead of growing by ~8 MB per frame.
7. Working set reaches a stable plateau.
8. Minimize pauses capture rather than closing AstralTFT.
9. Closing the TFT match window closes the diagnostic app after a short grace period.

## Report location

`%LOCALAPPDATA%\AstralTFT\Diagnostics\capture-benchmark-YYYYMMDD-HHMMSS.json`

The report contains AstralTFT process resource metrics and TFT window metadata. It does not contain captured images or TFT process memory.

## Why full-frame CPU readback exists at this gate

It gives us a simple correctness baseline and objective cost measurement. It is not the planned final hot path. Once this gate is measured, the next optimization is GPU-side/ROI-only change detection so unchanged 1080p frames do not cross the GPU→CPU boundary at all.
