# Native Library Inventory

## Table Of Contents

- [Required Runtime Libraries](#required-runtime-libraries)
- [Logical Names Vs File Names](#logical-names-vs-file-names)
- [Primary Native Sources](#primary-native-sources)
- [Managed Consumers](#managed-consumers)
- [Quick Verification Commands](#quick-verification-commands)

## Required Runtime Libraries

Expected platform files:

| Platform | Required Files |
|---|---|
| macOS | `libghostty-vt.dylib`, `libghostty-renderer-capi.dylib` |
| Linux | `libghostty-vt.so`, `libghostty-renderer-capi.so` |
| Windows | `ghostty-vt.dll`, `ghostty-renderer-capi.dll` |

This inventory aligns with package descriptions in:
- `src/RoyalTerminal.GhosttySharp.Native.OSX/RoyalTerminal.GhosttySharp.Native.OSX.csproj`
- `src/RoyalTerminal.GhosttySharp.Native.Linux64/RoyalTerminal.GhosttySharp.Native.Linux64.csproj`
- `src/RoyalTerminal.GhosttySharp.Native.Win64/RoyalTerminal.GhosttySharp.Native.Win64.csproj`

## Logical Names Vs File Names

Managed `LibraryImport` names:
- `ghostty-vt` (`GhosttyVtNative` internal lib name)
- `ghostty-renderer-capi` (`GhosttyRendererNative.LibraryName`)

OS loader maps these to platform file names via standard conventions or explicit resolver candidate paths.

## Primary Native Sources

- `external/ghostty` (submodule; `libghostty-vt`)
- `native/ghostty-renderer-capi` (renderer interop C API build)

Scripted build mapping:
- top-level `scripts/build-native.sh` / `scripts/build-native.ps1` build/copy `ghostty-vt` and `ghostty-renderer-capi`
- `scripts/run-integration-tests.sh` and `scripts/validate-macos.sh` validate `ghostty-vt`-focused flows

## Managed Consumers

- `RoyalTerminal.GhosttySharp` consumes `ghostty-vt`
- `RoyalTerminal.Rendering.Interop.Ghostty` consumes `ghostty-renderer-capi`
- `RoyalTerminal.Terminal.Vt.Ghostty` depends on official `ghostty-vt`

## Quick Verification Commands

Check built outputs in central native directory:
```bash
find native -maxdepth 3 -type f | rg "ghostty|renderer-capi"
```

Check packaged runtime assets:
```bash
find src/RoyalTerminal.GhosttySharp.Native.* -type f | rg "runtimes/.*/native/.*(ghostty|renderer-capi)"
```

Confirm demo/test output contains native files (after build):
```bash
find samples/RoyalTerminal.Demo/bin -type f | rg "ghostty|renderer-capi" | head
```

## Code Examples

### Verify runtime file presence for current RID

```bash
RID="osx-arm64" # adjust per platform
ls -la "src/RoyalTerminal.GhosttySharp.Native.OSX/runtimes/$RID/native"
```

### Fail fast when required libs are missing

```bash
RID="osx-arm64"
ROOT="src/RoyalTerminal.GhosttySharp.Native.OSX/runtimes/$RID/native"
for lib in libghostty-vt.dylib libghostty-renderer-capi.dylib; do
  test -f "$ROOT/$lib" || { echo "Missing $lib"; exit 1; }
done
```
