# RoyalTerminal Mac Native Tabbed Sample

Native macOS SwiftUI sample that embeds Ghostty surfaces directly in `NSView` with the full native Metal renderer path (`ghostty_surface_draw`).

This sample is intentionally separate from the managed `RoyalTerminal.GhosttySharp`
package surface. The .NET stack no longer ships a managed `libghostty` wrapper or
embedded Ghostty Avalonia controls; it uses official `libghostty-vt` for native VT
and keeps this Swift sample only as a direct native-host example.

## What it demonstrates

- Tabbed terminal UX with add/close tab actions.
- One Ghostty app runtime (`ghostty_app_t`) shared across tabs.
- Per-tab native surfaces (`ghostty_surface_t`) configured with:
  - `GHOSTTY_PLATFORM_MACOS`
  - `NSView` target binding
  - native action callbacks (`Render`, `SetTitle`, `CloseTab`, etc.)
- Full native rendering path via GhosttyKit (`libghostty.a` in xcframework).

## Requirements

- macOS 14+
- Xcode 16+ (or equivalent Swift 5.10 toolchain)
- This repository checked out with `external/ghostty` submodule present

## Build

```bash
swift build --package-path samples/RoyalTerminal.MacNativeTabbed
```

## Run

```bash
swift run --package-path samples/RoyalTerminal.MacNativeTabbed
```

If the process starts but no window becomes visible, use LaunchServices to foreground it:

```bash
bash samples/RoyalTerminal.MacNativeTabbed/run.sh
```

You can also open `samples/RoyalTerminal.MacNativeTabbed/Package.swift` in Xcode and run the `RoyalTerminalMacNativeTabbed` scheme.
