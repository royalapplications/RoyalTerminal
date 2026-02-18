# Native Library Usage Mapping

## Table Of Contents

- [libghostty](#libghostty)
- [libghostty-vt](#libghostty-vt)
- [libghostty-terminal](#libghostty-terminal)
- [ghostty-renderer-capi](#ghostty-renderer-capi)
- [Cross-Library Runtime Paths](#cross-library-runtime-paths)

## libghostty

Logical library name:
- `ghostty`

Managed entrypoints:
- `src/RoyalTerminal.GhosttySharp/Native/GhosttyNative.cs`

Primary managed API:
- `Ghostty.Initialize()` in `src/RoyalTerminal.GhosttySharp/Ghostty.cs`

Main usage:
- embedded Ghostty app/surface lifecycle for `GhosttyNativeTerminalControl` and `GhosttyRenderedTerminalControl`
- config/app/surface APIs from Ghostty embedded runtime

## libghostty-vt

Logical library name:
- `ghostty-vt`

Managed bindings:
- `src/RoyalTerminal.GhosttySharp/Native/GhosttyVtNative.cs`

Main usage:
- VT utility APIs (key encoder, OSC/SGR parser, paste safety helpers)
- built from `external/ghostty` via `zig build lib-vt` (scripted in `scripts/run-integration-tests.sh` and `scripts/validate-macos.sh`)
- integration tests validating VT utility behavior:
  - `tests/RoyalTerminal.IntegrationTests/KeyEncoderTests.cs`
  - `tests/RoyalTerminal.IntegrationTests/OscParserTests.cs`
  - `tests/RoyalTerminal.IntegrationTests/SgrParserTests.cs`
  - `tests/RoyalTerminal.IntegrationTests/PasteTests.cs`

## libghostty-terminal

Logical library name:
- `ghostty-terminal`

Managed bindings:
- `src/RoyalTerminal.GhosttySharp/Native/GhosttyTerminalNative.cs`

Primary consumer:
- `GhosttyVtProcessor` in `src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessor.cs`

Main usage:
- cross-platform native VT processor for `TerminalControl` native VT path
- mode-state reads (`app cursor`, `app keypad`, `alt screen`, `bracketed paste`)
- response/bell/title callbacks from native to managed
- row cell readback into `TerminalScreen`

## ghostty-renderer-capi

Logical library name:
- `ghostty-renderer-capi`

Managed bindings:
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Native/GhosttyRendererNative.cs`

Managed wrappers:
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderContext.cs`
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderSurface.cs`

Main usage:
- texture-interop rendering path used by Ghostty rendered mode
- target validation, frame begin/end, render-to-target, render-to-RGBA fallback

## Cross-Library Runtime Paths

High-level mapping by feature:

| Feature | Native libs required |
|---|---|
| Embedded Ghostty controls | `ghostty` |
| Native VT in `TerminalControl` | `ghostty-terminal` |
| VT utility integration tests | `ghostty-vt` |
| Texture interop rendering | `ghostty-renderer-capi` |
| Managed fallback VT/rendering | none (native optional) |

When debugging availability, validate both loader path and feature-specific library presence.

## Code Examples

### `libghostty` usage

```csharp
using RoyalTerminal.GhosttySharp;

if (!Ghostty.Initialize())
{
    throw new InvalidOperationException("Embedded Ghostty library is unavailable.");
}

GhosttyLibraryInfo info = Ghostty.GetInfo();
Console.WriteLine($"Ghostty {info.Version} ({info.BuildMode})");
```

### `libghostty-terminal` native VT usage

```csharp
TerminalControl control = new();
control.VtProcessorPreference = VtProcessorPreference.Native;
await control.StartSessionAsync(ptyOptions);
```

### `ghostty-renderer-capi` explicit path override

```bash
export GHOSTTY_RENDERER_CAPI_LIBRARY_PATH="$(pwd)/native/osx-arm64/libghostty-renderer-capi.dylib"
dotnet run --project samples/RoyalTerminal.Demo
```
