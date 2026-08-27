# Windows CI

AstralTFT uses GitHub Actions on `windows-latest` as the first Windows correctness gate.

## What every CI run verifies

1. Installs the current .NET 10 SDK.
2. Restores the full solution.
3. Builds all Windows/WPF/WinRT projects with .NET analyzers enabled.
4. Runs the dependency-free state-engine self-tests.
5. Runs the foundation/capture self-tests.
6. Publishes a framework-dependent `win-x64` diagnostic build.
7. Adds Windows verification/benchmark scripts and build metadata.
8. Generates a SHA-256 manifest.
9. Uploads the result as a GitHub Actions artifact for 14 days.

## Why framework-dependent first

The early diagnostic build intentionally requires the .NET 10 Desktop Runtime instead of bundling a self-contained runtime. This keeps artifacts small and makes failures easier to attribute while the capture stack is still changing. A self-contained installer can be added after the capture and recognition foundation is stable.

## Triggering CI

The workflow runs automatically on pushes and pull requests targeting `main` or `develop`. It can also be started manually from **Actions → Windows CI → Run workflow**.

## Real-hardware gate

Passing GitHub Actions proves that the Windows-specific code compiles and that deterministic tests pass. It does **not** prove Windows Graphics Capture performance against TFT. Real-hardware validation still runs on the target PC using the generated artifact and `run-capture-benchmark.ps1`.

## Failure policy

A failed Windows build blocks packaging. Runtime capture failures should remain isolated behind the capture subsystem/circuit breaker so a Riot patch cannot corrupt saved state or crash unrelated modules.
