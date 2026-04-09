---
title: VT Processors And Modes
---

# VT Processors And Modes

RoyalTerminal deliberately separates VT engine choice from the control and transport layers.

## Processor options

| Processor | Package | Characteristics |
| --- | --- | --- |
| `BasicVtProcessor` | `RoyalTerminal.Terminal.Vt.Managed` | Pure C#, always available, deterministic managed fallback |
| `GhosttyVtProcessor` | `RoyalTerminal.Terminal.Vt.Ghostty` | Uses the official `libghostty-vt` terminal and render-state APIs |

Both implementations expose the same `IVtProcessor` contract and both integrate with:

- title changes
- bell notifications
- query-response callbacks
- resize notifications
- terminal mode tracking
- snapshot export

## Preference modes

`VtProcessorPreference` controls factory behavior:

| Preference | Result |
| --- | --- |
| `Managed` | Always use `BasicVtProcessor` |
| `Auto` | Use the first working native provider, otherwise fall back to `BasicVtProcessor` |
| `Native` | Require a native provider and throw when none is available |

This policy is implemented by `DefaultVtProcessorFactory`.

## Native provider registration

The default native provider in this repository is `GhosttyVtProcessorProvider`.

```csharp
IVtProcessorFactory factory = new DefaultVtProcessorFactory(
    new INativeVtProcessorProvider[]
    {
        new GhosttyVtProcessorProvider()
    });
```

Provider order is policy. The first available provider that successfully creates a processor wins.

## Control and session integration

`TerminalControl` creates its VT processor from the active preference and keeps the current processor stable while a transport session is running.

`TerminalSessionService` then wires:

- `ResponseCallback`
- `BellCallback`
- `TitleCallback`

before the transport starts. That ordering matters because transports may emit data synchronously during startup.

## Terminal modes

`TerminalModeState` tracks the mode surface most relevant to input and terminal behavior:

- cursor visibility
- application cursor keys
- application keypad
- alternate screen
- bracketed paste

The input adapter uses the current mode source to encode keyboard and paste sequences correctly.

## Demo mode mapping

The Avalonia demo exposes three runtime modes:

| Demo mode | Processor preference |
| --- | --- |
| `Native VT` | `Native` |
| `Managed VT` | `Managed` |
| `Rendered (Auto VT)` | `Auto` |

Fallback order in the demo is deterministic:

`Native VT -> Managed VT -> Rendered (Auto VT)`

That means the demo always lands on a runnable mode even when native VT is unavailable on the current machine.

## When to use each mode

Use `Managed` when you want:

- deterministic managed-only behavior
- easier environments with no native VT dependency
- CI or package layouts where native assets are intentionally absent

Use `Auto` when you want:

- native VT when available
- resilient fallback without special-case host logic

Use `Native` when you want:

- strict Ghostty VT usage
- fast failure if native runtime assets or provider wiring are missing

## Validation focus

Any change to VT behavior should be verified against:

- `TerminalInputAdapterTests`
- `TerminalModeResolverTests`
- `TerminalControlTests`
- `MainWindowControllerModeStartupTests`
- integration parser tests in `tests/RoyalTerminal.IntegrationTests`

That coverage is important because VT changes can affect parser fidelity, input encoding, paste handling, resize behavior, and session callback symmetry all at once.
