# Modes And Settings

## Active Demo Modes

- `NativeVt`
- `ManagedVt`
- `RenderedAuto`

Files:

- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`
- `samples/RoyalTerminal.Demo/ViewModels/MainWindowViewModel.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`

## Settings Impact

- transport settings apply to all demo tabs
- there is no embedded-Ghostty-specific transport or rendering backend toggle anymore
- mode switching changes only VT preference and related labels/theme state

## Mode Behavior

- `NativeVt`: explicit upstream `libghostty-vt`
- `ManagedVt`: explicit `BasicVtProcessor`
- `RenderedAuto`: `TerminalControl` with `Auto` VT preference

Only one native-capability decision remains in the demo:
- whether official native VT (`libghostty-vt`) is available at runtime

## Removed Settings

The following were removed with the macOS-only Ghostty UI modes and the managed
`libghostty` wrapper layer:

- deleted embedded/native-host availability state
- texture interop toggle in the demo toolbar
- deleted backend-toggle commands
- deleted macOS-only demo mode entries
