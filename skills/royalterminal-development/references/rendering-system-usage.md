# Rendering System Usage

## Default Product Rendering Path

- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`
- `src/RoyalTerminal.Rendering.Skia`

This is the normal rendering path for:

- `NativeVt`
- `ManagedVt`
- `RenderedAuto`

## Optional Low-Level Interop Path

Renderer interop remains available through:

- `RoyalTerminal.Rendering.Interop.Ghostty`
- `RoyalTerminal.Rendering.Interop.Ghostty.Skia`
- `RoyalTerminal.Avalonia.Rendering.GhosttyInterop`

Those packages are no longer wrapped by a built-in macOS-only Ghostty control.
