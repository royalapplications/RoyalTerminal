# VT Implementations And Providers

## Table Of Contents

- [Managed Implementation](#managed-implementation)
- [Native Implementation](#native-implementation)
- [Native Provider](#native-provider)
- [Behavior Comparison](#behavior-comparison)
- [Performance And Reliability Notes](#performance-and-reliability-notes)
- [Implementation Hotspots](#implementation-hotspots)

## Managed Implementation

Type:
- `BasicVtProcessor`
- `src/RoyalTerminal.Terminal.Vt.Managed/Terminal/BasicVtProcessor.cs`

Characteristics:
- pure C# VT parser and screen mutator
- always available (no native dependencies)
- supports broad VT behavior including:
  - CSI/OSC processing
  - SGR and cursor movement
  - alternate buffer and scroll regions
  - DEC private modes (cursor keys, bracketed paste, mouse-related mode effects)
  - title updates, bell, and query responses through callbacks

Operational behavior:
- chunk-safe parser state machine
- updates `TerminalModeState` and raises `ModeChanged` on transitions
- sends VT responses through `ResponseCallback`

## Native Implementation

Type:
- `GhosttyVtProcessor`
- `src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessor.cs`

Native bridge:
- `GhosttyTerminal`
- `GhosttyRenderState`
- `src/RoyalTerminal.GhosttySharp/GhosttyTerminal.cs`
- `src/RoyalTerminal.GhosttySharp/GhosttyRenderState.cs`

Characteristics:
- wraps official `libghostty-vt` terminal and render-state APIs
- uses official native effect/response callbacks
- synchronizes native render state back into managed `TerminalScreen`
- supports pixel-aware resize and viewport/state queries via upstream APIs

Availability probe:
- `GhosttyVtProcessor.IsAvailable()` delegates to native availability checks

Lifecycle highlights:
- terminal handle created in constructor
- callbacks re-established on reset/recreate paths
- disposes native terminal handle on dispose

## Native Provider

Type:
- `GhosttyVtProcessorProvider`
- `src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessorProvider.cs`

Behavior:
- `IsAvailable` forwards to `GhosttyVtProcessor.IsAvailable()`
- `Create(screen)` returns a new `GhosttyVtProcessor`

Typical registration:
```csharp
INativeVtProcessorProvider[] providers = [new GhosttyVtProcessorProvider()];
IVtProcessorFactory factory = new DefaultVtProcessorFactory(providers);
```

## Behavior Comparison

| Aspect | `BasicVtProcessor` | `GhosttyVtProcessor` |
|---|---|---|
| dependency | managed only | requires `libghostty-vt` |
| availability | always | platform/native-library dependent |
| mode flags source | managed parser state | native terminal state query |
| response generation | managed logic | official native effect/response callbacks |
| screen sync | direct managed writes | official render-state readback into managed screen |

## Performance And Reliability Notes

- native path typically gives higher protocol fidelity and mature VT behavior.
- managed path is deterministic fallback and must stay robust for all platforms.
- both implementations must keep callback semantics identical from session/control perspective.
- regression risk is highest around mode transitions, wide characters, and query response forwarding.

## Implementation Hotspots

Review these areas when changing VT implementations:
- mode transition detection and `ModeChanged` emission
- response callback emission for DSR/DA/DECRQM
- resize/reflow synchronization with `TerminalScreen`
- alternate screen toggles and cursor visibility updates
- reset logic consistency with callback re-wiring

## Code Examples

### Select native processor when available

```csharp
TerminalScreen screen = new(columns: 120, rows: 40, scrollbackLimit: 10_000);
IVtProcessor processor = GhosttyVtProcessor.IsAvailable()
    ? new GhosttyVtProcessor(screen)
    : new BasicVtProcessor(screen);
```

### Register native provider with default factory

```csharp
IVtProcessorFactory factory = new DefaultVtProcessorFactory(
    new INativeVtProcessorProvider[]
    {
        new GhosttyVtProcessorProvider()
    });
```

### Process output bytes and read mode state

```csharp
processor.Process(Encoding.UTF8.GetBytes("\u001b[?2004h")); // bracketed paste on
TerminalModeState mode = processor.ModeState;
Console.WriteLine(mode.BracketedPaste); // true
```
