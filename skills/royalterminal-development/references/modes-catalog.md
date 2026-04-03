# Modes Catalog

RoyalTerminal now exposes three product modes in the Avalonia demo and shared UI guidance:

- `NativeVt`
- `ManagedVt`
- `RenderedAuto`

## Mode Enum

File:

- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`

Enum values:

- `NativeVt = 0`
- `ManagedVt = 1`
- `RenderedAuto = 2`

Resolver cycle order:

- `NativeVt -> ManagedVt -> RenderedAuto`

## Capability Model

File:

- `samples/RoyalTerminal.Demo/Services/TerminalModeResolver.cs`

Capabilities tracked:

- `NativeVtAvailable`
- `ManagedVtAvailable`

The removed macOS-only embedded Ghostty UI modes are no longer part of the mode catalog.

## Demo Labels

- `Native VT`
- `Managed VT`
- `Rendered`

## Notes

- `RenderedAuto` uses `TerminalControl` with `VtProcessorPreference.Auto`.
- `NativeVt` uses `TerminalControl` with `VtProcessorPreference.Native`.
- `ManagedVt` uses `TerminalControl` with `VtProcessorPreference.Managed`.
