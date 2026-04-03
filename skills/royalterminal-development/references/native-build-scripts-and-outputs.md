# Native Build Scripts And Outputs

## Table Of Contents

- [Top-Level Build Scripts](#top-level-build-scripts)
- [Subcomponent Build Scripts](#subcomponent-build-scripts)
- [libghostty-vt Build Path](#libghostty-vt-build-path)
- [What Gets Built](#what-gets-built)
- [Where Artifacts Are Copied](#where-artifacts-are-copied)
- [Platform Notes](#platform-notes)
- [Post-Build Checks](#post-build-checks)

## Top-Level Build Scripts

Primary scripts:
- `scripts/build-native.sh` (macOS/Linux)
- `scripts/build-native.ps1` (Windows)

Top-level responsibilities:
- verify prerequisites (`zig`, initialized submodule)
- build `ghostty` shared lib from `external/ghostty`
- build `ghostty-vt` from the same upstream Ghostty build graph
- build `ghostty-renderer-capi`
- copy artifacts to runtime package directories and central `native/<rid>/`
- copy native headers into `native/include/`

## Subcomponent Build Scripts

Direct build entrypoints:
- `native/ghostty-renderer-capi/build.sh`

Supported modes (script-dependent):
- debug
- release (`ReleaseFast`)
- release-safe
- clean
- test
- renderer sample (renderer script only)

Use direct scripts for focused component iteration; use top-level scripts for full runtime sync.

## libghostty-vt Build Path

Current repository scripts that build `libghostty-vt`:
- `scripts/build-native.sh`
- `scripts/build-native.ps1`
- `scripts/run-integration-tests.sh` (`zig build lib-vt -Doptimize=ReleaseFast`)
- `scripts/validate-macos.sh` (`zig build lib-vt -Doptimize=ReleaseFast`)

Output behavior in these scripts:
- build from `external/ghostty`
- copy `libghostty-vt` into `native/<rid>/`
- verify symbols and test usage through integration test runs

Important:
- top-level `scripts/build-native.sh` / `scripts/build-native.ps1` now copy `libghostty-vt` into runtime/package folders.
- when VT utility APIs or integration tests are in scope, still run one of the dedicated scripts above because they validate the VT library in addition to building it.

## What Gets Built

Core output groups from top-level build scripts:
- Ghostty embedded library (`ghostty`)
- official VT library (`ghostty-vt`)
- renderer interop library (`ghostty-renderer-capi`)

Header outputs copied to `native/include/` include:
- `ghostty.h`
- `ghostty/vt/*`
- `ghostty_renderer.h`

## Where Artifacts Are Copied

Top-level scripts copy each built library to:

1. central artifact folder:
- `native/<rid>/`

2. runtime package folders:
- `src/RoyalTerminal.GhosttySharp.Native.OSX/runtimes/<rid>/native/`
- `src/RoyalTerminal.GhosttySharp.Native.Linux64/runtimes/<rid>/native/`
- `src/RoyalTerminal.GhosttySharp.Native.Win64/runtimes/<rid>/native/`

This dual copy supports both direct development checks and NuGet runtime packaging.

`libghostty-vt` copy behavior:
- top-level native build scripts copy it into both `native/<rid>/` and runtime package folders
- `scripts/run-integration-tests.sh` and `scripts/validate-macos.sh` also validate VT-specific behavior

## Platform Notes

- `scripts/build-native.sh` supports macOS/Linux directly.
- `scripts/build-native.ps1` is prepared for Windows compatibility (upstream Ghostty support caveats still apply).
- Windows script includes architecture switch (`-Arch x64|arm64`).
- macOS path in `scripts/build-native.sh` auto-detects `osx-arm64` vs `osx-x64`.
- Linux path in `scripts/build-native.sh` currently targets `linux-x64` RID in script logic.
- `libghostty-vt` scripted build path is currently macOS/Linux-focused.

## Post-Build Checks

After script completion:

1. Verify libraries exist in `native/<rid>/`.
2. Verify same libraries are present in package runtime folders.
3. If VT utility APIs/tests are required, verify `libghostty-vt` in `native/<rid>/`.
4. Run managed build:
   - `dotnet build RoyalTerminal.sln -c Release`
5. Run smoke execution:
   - `dotnet run --project samples/RoyalTerminal.Demo`
6. Run tests (at minimum transport/native/render focused suites).

For full command matrix, see:
- [`build-test-validation.md`](build-test-validation.md)

## Code Examples

### Full native build and copy

```bash
bash scripts/build-native.sh --clean --release
```

### Component-only iteration loop

```bash
cd external/ghostty
zig build -Doptimize=ReleaseFast -Dapp-runtime=none

cd ../../native/ghostty-renderer-capi
bash build.sh release
bash build.sh test
```

### Build `libghostty-vt` for integration tests

```bash
bash scripts/run-integration-tests.sh               # build libghostty-vt + run tests
bash scripts/run-integration-tests.sh --skip-build --verbose
```

### Validate copied runtime outputs

```bash
find native -maxdepth 3 -type f | rg "ghostty|renderer-capi"
find src/RoyalTerminal.GhosttySharp.Native.* -type f | rg "runtimes/.*/native"
```
