# VT Demo Mapping And Validation

## Demo Mode Mapping

Files:

- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`
- `samples/RoyalTerminal.Demo/ViewModels/MainWindowViewModel.cs`

Mapping:

- `NativeVt` -> `TerminalControl` + `VtProcessorPreference.Native`
- `ManagedVt` -> `TerminalControl` + `VtProcessorPreference.Managed`
- `RenderedAuto` -> `TerminalControl` + `VtProcessorPreference.Auto`

## Validation Focus

When changing demo mode behavior, validate:

- startup tab creation for all supported modes
- mode cycle order
- fallback from `NativeVt` to `ManagedVt` when native VT is unavailable
- tab header tooltip/glyph/color mapping
- transport settings visibility for all three modes

## Removed Paths

The demo no longer hosts embedded Ghostty surface controls, so validation no longer includes deleted macOS-only control modes or backend toggles.
