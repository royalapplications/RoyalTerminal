# Native Loader Resolution

## Table Of Contents

- [Ghostty VT Loader](#ghostty-vt-loader)
- [Renderer CAPI Loader](#renderer-capi-loader)
- [Probe Order Summary](#probe-order-summary)
- [Environment Overrides](#environment-overrides)
- [Availability Probes](#availability-probes)
- [Debug Checklist](#debug-checklist)

## Ghostty VT Loader

Loader class:
- `NativeLibraryLoader`
- `src/RoyalTerminal.GhosttySharp/Native/NativeLibraryLoader.cs`

Registered for assembly:
- `typeof(GhosttyVtNative).Assembly`

Logical library targeted:
- `ghostty-vt`

Behavior:
- sets `NativeLibrary.SetDllImportResolver(...)` once (thread-safe init)
- resolves runtime identifier from OS + process architecture
- computes platform filename (`ghostty-vt.dll`, `libghostty-vt.dylib`, `libghostty-vt.so`)

## Renderer CAPI Loader

Loader class:
- `GhosttyRendererNativeLibraryLoader`
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Native/GhosttyRendererNativeLibraryLoader.cs`

Registered for assembly:
- `typeof(GhosttyRendererNative).Assembly`

Logical library targeted:
- `ghostty-renderer-capi`

Behavior:
- gathers candidate absolute paths with deduplication
- tries explicit env candidates first
- then runtime package and base/assembly directory candidates
- then assembly/default native loader fallback

## Probe Order Summary

Core (`ghostty-vt`) probe order:
1. `NativeLibrary.TryLoad(libraryName, assembly, searchPath, ...)`
2. `AppContext.BaseDirectory/runtimes/<rid>/native/<file>`
3. `AppContext.BaseDirectory/<file>`
4. `assemblyDir/runtimes/<rid>/native/<file>`
5. `assemblyDir/<file>`
6. fallback `NativeLibrary.TryLoad(libraryName)`

Renderer (`ghostty-renderer-capi`) probe order:
1. `GHOSTTY_RENDERER_CAPI_LIBRARY_PATH` (absolute or relative normalized)
2. `GHOSTTY_RENDERER_CAPI_LIBRARY_DIR/<file>`
3. `AppContext.BaseDirectory/runtimes/<rid>/native/<file>`
4. `AppContext.BaseDirectory/<file>`
5. `assemblyDir/runtimes/<rid>/native/<file>`
6. `assemblyDir/<file>`
7. default assembly/global loader fallback

## Environment Overrides

Supported renderer override variables:
- `GHOSTTY_RENDERER_CAPI_LIBRARY_PATH`
- `GHOSTTY_RENDERER_CAPI_LIBRARY_DIR`

No equivalent explicit override variable is currently defined for the VT loader.

## Availability Probes

Availability APIs:
- `GhosttyVtProcessor.IsAvailable()` (wraps official `libghostty-vt` availability)

Common exception signals before graceful fallbacks:
- `DllNotFoundException`
- `EntryPointNotFoundException`
- `BadImageFormatException`

## Debug Checklist

1. Confirm runtime file exists for current RID/arch.
2. Confirm process architecture matches binary architecture.
3. Confirm loader probe paths contain expected files.
4. For renderer path issues, set explicit `GHOSTTY_RENDERER_CAPI_LIBRARY_PATH` and retry.
5. Rebuild native artifacts and clear stale output folders when symbols mismatch.

## Code Examples

### Configure explicit renderer library path

```bash
export GHOSTTY_RENDERER_CAPI_LIBRARY_PATH="/absolute/path/to/libghostty-renderer-capi.dylib"
dotnet run --project samples/RoyalTerminal.Demo
```

### Configure renderer library directory

```bash
export GHOSTTY_RENDERER_CAPI_LIBRARY_DIR="/absolute/path/to/native/osx-arm64"
dotnet run --project samples/RoyalTerminal.Demo
```

### Availability probe at startup

```csharp
bool nativeVtAvailable = GhosttyVtProcessor.IsAvailable();

Console.WriteLine($"NativeVT={nativeVtAvailable}");
```
