# Native Libraries Setup

This is the native reference entrypoint. Read this first, then open only the needed native sub-file.

## Table Of Contents

- [Scope](#scope)
- [Load Order](#load-order)
- [Decision Guide](#decision-guide)
- [Build-To-Run Workflow](#build-to-run-workflow)
- [Critical Invariants](#critical-invariants)
- [Validation Gate](#validation-gate)

## Scope

The native reference set covers:
- required native libraries and platform filenames
- managed wrapper to native library mapping
- loader probing and environment overrides
- NuGet runtime asset packaging and output copy behavior
- native build scripts and expected outputs
- runtime troubleshooting and recovery patterns

## Load Order

1. [`native-library-inventory.md`](native-library-inventory.md)
2. [`native-library-usage-mapping.md`](native-library-usage-mapping.md)
3. [`native-loader-resolution.md`](native-loader-resolution.md)
4. [`native-package-layout.md`](native-package-layout.md)
5. [`native-build-scripts-and-outputs.md`](native-build-scripts-and-outputs.md)
6. [`native-runtime-checklist-and-troubleshooting.md`](native-runtime-checklist-and-troubleshooting.md)

## Decision Guide

| If you are changing... | Read first | Then read |
|---|---|---|
| list of required native files | [`native-library-inventory.md`](native-library-inventory.md) | [`native-package-layout.md`](native-package-layout.md) |
| managed/native binding code | [`native-library-usage-mapping.md`](native-library-usage-mapping.md) | [`native-loader-resolution.md`](native-loader-resolution.md) |
| loader probing/environment variables | [`native-loader-resolution.md`](native-loader-resolution.md) | [`native-runtime-checklist-and-troubleshooting.md`](native-runtime-checklist-and-troubleshooting.md) |
| build scripts or copy outputs | [`native-build-scripts-and-outputs.md`](native-build-scripts-and-outputs.md) | [`native-package-layout.md`](native-package-layout.md) |
| NuGet runtime assets/targets | [`native-package-layout.md`](native-package-layout.md) | [`native-runtime-checklist-and-troubleshooting.md`](native-runtime-checklist-and-troubleshooting.md) |
| runtime load failures | [`native-runtime-checklist-and-troubleshooting.md`](native-runtime-checklist-and-troubleshooting.md) | [`native-loader-resolution.md`](native-loader-resolution.md) |

## Build-To-Run Workflow

1. Initialize submodule:
   - `git submodule update --init --recursive`
2. Build native libs:
   - macOS/Linux: `bash scripts/build-native.sh --release`
   - Windows: `pwsh scripts/build-native.ps1 -Release`
3. Build `libghostty-vt` when VT utility APIs/tests are in scope:
   - macOS/Linux: `bash scripts/run-integration-tests.sh` (build + test)
   - macOS/Linux reuse existing library: `bash scripts/run-integration-tests.sh --skip-build`
   - macOS validation path: `bash scripts/validate-macos.sh`
4. Confirm outputs under both:
   - `native/<rid>/`
   - `src/RoyalTerminal.GhosttySharp.Native.*/runtimes/<rid>/native/`
5. Build managed solution: `dotnet build RoyalTerminal.sln -c Release`
6. Run demo or tests with expected RID/architecture.

## Critical Invariants

- runtime packages must contain all required native files for target RID.
- loader probe order and environment override behavior must remain deterministic.
- build scripts must copy outputs to runtime package directories and central `native/<rid>/` directories.
- top-level `scripts/build-native.sh` and `scripts/build-native.ps1` copy `ghostty`, `ghostty-vt`, and `ghostty-renderer-capi`.
- `scripts/build-native.sh` currently uses `linux-x64` RID on Linux; handle `linux-arm64` outputs through dedicated build/copy steps when needed.
- architecture must match process runtime (`x64` vs `arm64`).

## Validation Gate

Always validate:
- native build scripts complete without missing artifacts
- managed build succeeds after native copy step
- runtime availability checks pass (`GhosttyVtProcessor.IsAvailable()`)
- full test suite for shared/native contract changes

Details:
- [`native-runtime-checklist-and-troubleshooting.md`](native-runtime-checklist-and-troubleshooting.md)
- [`build-test-validation.md`](build-test-validation.md)

## Code Examples

### Full setup from clean clone

```bash
git submodule update --init --recursive

# Build native artifacts and copy to runtime package folders
bash scripts/build-native.sh --release
# Build/verify libghostty-vt for VT utility tests when needed
bash scripts/run-integration-tests.sh

# Build managed solution
dotnet build RoyalTerminal.sln -c Release

# Run demo
dotnet run --project samples/RoyalTerminal.Demo
```

### Windows setup flow

```powershell
git submodule update --init --recursive
pwsh scripts/build-native.ps1 -Release

dotnet build RoyalTerminal.sln -c Release
dotnet run --project samples/RoyalTerminal.Demo
```

### Verify expected files after native build

```bash
find native -maxdepth 3 -type f | rg 'ghostty|renderer-capi'
```
