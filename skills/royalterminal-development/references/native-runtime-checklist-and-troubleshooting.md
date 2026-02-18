# Native Runtime Checklist And Troubleshooting

## Table Of Contents

- [Runtime Checklist](#runtime-checklist)
- [Availability Checks](#availability-checks)
- [Common Failure Patterns](#common-failure-patterns)
- [Targeted Recovery Actions](#targeted-recovery-actions)
- [Validation Commands](#validation-commands)

## Runtime Checklist

1. Initialize submodule:
   - `git submodule update --init --recursive`
2. Build native libraries:
   - macOS/Linux: `bash scripts/build-native.sh --release`
   - Windows: `pwsh scripts/build-native.ps1 -Release`
3. Build `libghostty-vt` when VT utility APIs/tests are needed:
   - macOS/Linux: `bash scripts/run-integration-tests.sh`
   - macOS validation suite: `bash scripts/validate-macos.sh`
4. Verify runtime files exist for current RID under package runtime folders.
5. Build managed solution:
   - `dotnet build RoyalTerminal.sln -c Release`
6. Run availability probes in runtime flow:
   - `Ghostty.Initialize()` for embedded ghostty usage
   - `GhosttyVtProcessor.IsAvailable()` for native VT usage
7. Run demo/tests for the changed feature path.

## Availability Checks

Useful checks per feature:

| Feature | Probe |
|---|---|
| Embedded Ghostty mode | `Ghostty.Initialize()` |
| Native VT mode | `GhosttyVtProcessor.IsAvailable()` |
| Renderer interop mode | successful load through `GhosttyRendererNativeLibraryLoader` |

If one feature fails while others work, focus on that feature-specific native library first.

## Common Failure Patterns

| Error | Typical Cause | First Check |
|---|---|---|
| `DllNotFoundException` | missing file or probe-path miss | runtime folder contents + probe order |
| `EntryPointNotFoundException` | binary/API version mismatch | stale native library in output |
| `BadImageFormatException` | architecture mismatch | process RID/arch vs native binary arch |
| native VT unavailable | `ghostty-terminal` missing or incompatible | `native/<rid>/` + runtime copy targets |
| texture interop unavailable | `ghostty-renderer-capi` unresolved | renderer env override path |
| SSH/transport unaffected but embedded mode missing | only `ghostty` load failing | core loader resolution path |

## Targeted Recovery Actions

- Clean and rebuild native libs:
  - `bash scripts/build-native.sh --clean --release`
- Rebuild VT utility library when VT integration tests or VT utility bindings fail:
  - `bash scripts/run-integration-tests.sh`
- Remove stale managed output directories and rebuild.
- Verify correct RID-specific files are copied into consuming app output.
- For renderer failures, set explicit path:
  - `GHOSTTY_RENDERER_CAPI_LIBRARY_PATH=/absolute/path/to/libghostty-renderer-capi.<ext>`
- Confirm process architecture (`x64`/`arm64`) matches native file architecture.
- Re-run with release build configuration for deterministic runtime layout.

## Validation Commands

Baseline validation:
```bash
dotnet build RoyalTerminal.sln -c Release
dotnet test RoyalTerminal.sln -c Release
```

Renderer/native focused subset:
```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "RenderingInteropTests|RenderingSkiaInteropTests|RenderingAvaloniaAdapterTests|GhosttyComponentTests|PackageBoundaryTests|WindowsArm64NativePackagingTests"
```

Integration checks:
```bash
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "TerminalNativeTests|GhosttyWrapperIntegrationTests"
```

For full build script and command coverage:
- `references/build-test-validation.md`

## Code Examples

### Quick runtime smoke script

```bash
set -euo pipefail

bash scripts/build-native.sh --release
bash scripts/run-integration-tests.sh
dotnet build RoyalTerminal.sln -c Release
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "GhosttyComponentTests|RenderingInteropTests|PackageBoundaryTests"
```

### Diagnose architecture mismatch

```bash
# macOS
file native/osx-arm64/libghostty.dylib

# Linux
file native/linux-x64/libghostty.so
```

### Force renderer resolution during troubleshooting

```bash
export GHOSTTY_RENDERER_CAPI_LIBRARY_PATH="$(pwd)/native/osx-arm64/libghostty-renderer-capi.dylib"
DOTNET_ENVIRONMENT=Development dotnet run --project samples/RoyalTerminal.Demo
```
