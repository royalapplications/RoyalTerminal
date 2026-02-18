# VT Contracts And Preferences

## Table Of Contents

- [Primary Contracts](#primary-contracts)
- [IVtProcessor Surface](#ivtprocessor-surface)
- [Preference Modes](#preference-modes)
- [Mode-State Contract](#mode-state-contract)
- [Contract Invariants](#contract-invariants)
- [Extension Rules](#extension-rules)

## Primary Contracts

Source files:
- `src/RoyalTerminal.Terminal/Terminal/IVtProcessor.cs`
- `src/RoyalTerminal.Terminal/Terminal/IVtProcessorFactory.cs`
- `src/RoyalTerminal.Terminal/Terminal/INativeVtProcessorProvider.cs`
- `src/RoyalTerminal.Terminal/Terminal/VtProcessorPreference.cs`

Core abstractions:
- `IVtProcessor`
- `IVtProcessorFactory`
- `INativeVtProcessorProvider`
- `VtProcessorPreference`

## IVtProcessor Surface

`IVtProcessor` responsibilities:
- process terminal output bytes into `TerminalScreen` state
- expose cursor and mode flags used by renderer/input paths
- emit mode changes (`ModeChanged`)
- emit protocol callbacks for host side:
  - `ResponseCallback` (DSR/DA/DECRQM/etc.)
  - `BellCallback`
  - `TitleCallback`
- handle resize (`NotifyResize` overloads)
- reset parser/terminal state (`Reset`)

Mode-related properties used downstream:
- `ApplicationCursorKeys`
- `ApplicationKeypad`
- `AlternateScreen`
- `BracketedPaste`

These properties directly affect key encoding, mouse behavior, paste behavior, and UI interaction semantics.

## Preference Modes

Enum:
- `VtProcessorPreference`

Values:
- `Auto`
  - prefer native providers if available
  - fall back to managed implementation

- `Managed`
  - force `BasicVtProcessor`
  - no native dependency

- `Native`
  - require native provider
  - throw if native cannot be created

## Mode-State Contract

`TerminalModeState` is the normalized mode snapshot shared between VT and input layers.

Mode propagation chain:
1. processor mode flags are updated during `Process(...)`
2. processor raises `ModeChanged` when snapshot differs
3. session service exposes mode source (`ITerminalModeSource`)
4. input adapter encodes keys/pointer behavior based on that mode snapshot

## Contract Invariants

- `Process(...)` should be safe on arbitrary chunk boundaries and mixed control/data bytes.
- callbacks are optional and null-safe.
- `ModeChanged` should only raise on real state transitions.
- resize and reset must leave processor state internally consistent.
- dispose must release resources and unsubscribe native callbacks where applicable.

## Extension Rules

If adding a new VT implementation/provider:

1. Implement full `IVtProcessor` surface, including callbacks and mode flags.
2. Implement provider with deterministic `IsAvailable` logic.
3. Register provider in `DefaultVtProcessorFactory` call sites.
4. Keep `Auto` and `Native` semantics unchanged.
5. Add tests for mode changes, callbacks, resize, reset, and availability behavior.

## Code Examples

### Minimal `IVtProcessor` implementation skeleton

```csharp
public sealed class NoOpVtProcessor : IVtProcessor
{
    public int CursorCol => 0;
    public int CursorRow => 0;
    public bool CursorVisible => true;
    public bool ApplicationCursorKeys => false;
    public bool ApplicationKeypad => false;
    public bool AlternateScreen => false;
    public bool BracketedPaste => false;
    public TerminalModeState ModeState => new(true, false, false, false, false);

    public event EventHandler<TerminalModeState>? ModeChanged;
    public Action<byte[]>? ResponseCallback { get; set; }
    public Action? BellCallback { get; set; }
    public Action<string>? TitleCallback { get; set; }

    public void Process(ReadOnlySpan<byte> data) { }
    public void NotifyResize(int columns, int rows) { }
    public void NotifyResize(int columns, int rows, int widthPx, int heightPx) { }
    public void Reset() { }
    public void Dispose() { }
}
```

### Create processor with explicit preference

```csharp
IVtProcessor processor = vtFactory.Create(screen, VtProcessorPreference.Auto);
```

### Hook query-response callback

```csharp
processor.ResponseCallback = responseBytes => sessionService.SendInput(responseBytes);
```
