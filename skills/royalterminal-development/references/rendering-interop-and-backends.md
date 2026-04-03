# Rendering Interop And Backends

## Current Scope

RoyalTerminal keeps the Ghostty renderer interop stack as a low-level integration surface:

- `src/RoyalTerminal.Rendering.Interop.Ghostty`
- `src/RoyalTerminal.Rendering.Interop.Ghostty.Skia`
- `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop`

These packages remain for advanced native render-target integration and CPU RGBA fallback behavior.

## Important Boundary

The macOS-only embedded Ghostty Avalonia control package was removed.

That means:

- there is no built-in embedded Ghostty terminal control
- renderer interop packages are no longer exposed through a product mode in the demo

## Primary Consumer Model

Use the interop packages only when you explicitly need:

- `ghostty-renderer-capi`
- `GhosttyRenderContext`
- `GhosttyRenderSurface`
- `SkiaInteropRenderer`
- `TerminalTextureInteropDrawHandler`

Do not treat them as the default rendering path for `TerminalControl`; the default UI path is the Skia cell renderer in `RoyalTerminal.Avalonia`.
