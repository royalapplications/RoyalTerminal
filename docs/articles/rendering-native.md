---
title: Rendering And Native Runtime
---

# Rendering And Native Runtime

RoyalTerminal splits text shaping, CPU rendering, renderer interop, and OS-specific native asset packaging into separate packages so applications can adopt only what they need.

## CPU rendering path

The default terminal drawing path is:

1. VT processor updates `TerminalScreen`
2. `RoyalTerminal.Rendering.Text` resolves fonts and shaping
3. `RoyalTerminal.Rendering.Skia` paints cells, decorations, cursor state, and glyphs
4. `RoyalTerminal.Avalonia` presents the result inside the control

Key pieces:

- `HarfBuzzTextShaper`
- `TerminalFontResolver`
- `GlyphCache`
- `SkiaTerminalRenderer`

## Text shaping and diagnostics

Text shaping is backed by HarfBuzz and is designed for grid-safe terminal behavior rather than general rich text layout.

The sample app exposes diagnostics toggles via environment variables:

- `ROYALTERMINAL_DISABLE_TEXT_SHAPING=1`
- `ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1`

These are useful when validating fallback behavior, shaping issues, or render hot paths.

## Ghostty renderer interop path

The optional interop stack is separate from the default CPU path:

- `RoyalTerminal.Rendering.Contracts`
- `RoyalTerminal.Rendering.Interop.Ghostty`
- `RoyalTerminal.Rendering.Interop.Ghostty.Skia`
- `RoyalTerminal.Avalonia.Rendering.GhosttyInterop`

That stack is for hosts that need the Ghostty renderer integration surface and the required native bridge pieces, including render-target acquisition and texture handle extraction.

## Native VT and renderer assets

Native artifacts are distributed in dedicated runtime packages:

- `RoyalTerminal.GhosttySharp.Native.OSX`
- `RoyalTerminal.GhosttySharp.Native.Win64`
- `RoyalTerminal.GhosttySharp.Native.Linux64`

Those packages carry the platform binaries for:

- `libghostty-vt`
- `ghostty-vt.dll`
- `libghostty-renderer-capi`
- `ghostty-renderer-capi.dll`

depending on the target OS and RID.

## Native build entry points

The repository-level native build entry points are:

- `scripts/build-native.sh`
- `scripts/build-native.ps1`
- `scripts/run-integration-tests.sh`
- `scripts/validate-macos.sh`
- `native/ghostty-renderer-capi/build.sh`

The managed stack consumes the resulting artifacts, but this documentation intentionally does not describe the internal contents of the `external/ghostty` submodule.

## Packaging model

The repository uses:

- dedicated native-only NuGet packages for runtime assets
- `build` and `buildTransitive` targets for copy/setup behavior
- CI jobs that build native artifacts first, then stage them for managed interop and tests

That separation lets the managed packages remain clean while still shipping the required native payloads.

## When to stay on the CPU path

Use the default Skia path when you want:

- the simplest cross-platform host setup
- no renderer interop dependency
- a smaller package graph
- easier debugging of terminal output and rendering behavior

Move to the interop path only if your host architecture explicitly needs Ghostty renderer integration.
