# VT Input Mode Propagation

## Table Of Contents

- [Mode Resolution Order](#mode-resolution-order)
- [Keyboard Encoding Path](#keyboard-encoding-path)
- [Mode-Sensitive Behaviors](#mode-sensitive-behaviors)
- [Pointer And Paste Interactions](#pointer-and-paste-interactions)
- [Routing Fallback Rules](#routing-fallback-rules)
- [Validation Targets](#validation-targets)

## Mode Resolution Order

Input adapter implementation:
- `DefaultTerminalInputAdapter`
- `src/RoyalTerminal.Avalonia/Services/DefaultTerminalInputAdapter.cs`

Mode state resolution precedence:
1. `sessionService.ModeSource` (endpoint mode source or VT bridge)
2. direct `vtProcessor.ModeState`
3. default conservative mode snapshot

Why this matters:
- endpoint-backed controls can override mode semantics cleanly
- standalone paths still read VT mode state when endpoint source is absent

## Keyboard Encoding Path

Encoder:
- `TerminalKeySequenceEncoder`
- `src/RoyalTerminal.Avalonia/Services/TerminalKeySequenceEncoder.cs`

Flow for key-down in fallback transport path:
1. adapter resolves mode state
2. encoder tries to encode key/modifier combination
3. encoded sequence is sent to `ITerminalSessionService.SendInput(...)`

When endpoint input sink exists:
- adapter forwards normalized `TerminalKeyEvent` directly to endpoint
- fallback encoder path is bypassed

## Mode-Sensitive Behaviors

`TerminalKeySequenceEncoder` uses mode flags to switch encoding behavior:

- `ApplicationCursorKeys`
  - arrows/home/end use SS3 or CSI sequences based on mode

- `ApplicationKeypad`
  - keypad keys switch between application and normal sequences

Additional behaviors:
- control chords and function keys with modifier parameter mapping
- alt-prefix handling
- explicit filtering of pure modifier keys

## Pointer And Paste Interactions

Pointer path is primarily in `TerminalControl`, but mode propagation affects it through session mode source and VT mode tracking:
- mouse reporting behavior depends on active mouse mode state (`TerminalMouseModeState`)
- when mouse reporting is active, selection behavior is suppressed in favor of encoded mouse events

Paste path:
- bracketed paste behavior uses current mode source (`BracketedPaste`)
- fallback to VT processor property if mode source is unavailable

## Routing Fallback Rules

Input adapter routing priorities:
- key down/up: endpoint sink first, then encoded transport fallback
- text input: endpoint sink first, then session input string path

Session service send priorities (downstream):
- endpoint -> active transport -> legacy direct PTY

These two layers together preserve consistent behavior across endpoint-backed and transport-backed modes.

## Validation Targets

Primary tests:
- `tests/RoyalTerminal.Tests/TerminalInputAdapterTests.cs`
- `tests/RoyalTerminal.Tests/TerminalMouseProtocolTests.cs`
- `tests/RoyalTerminal.Tests/TerminalControlHeadlessInteractionTests.cs`
- `tests/RoyalTerminal.Tests/TerminalSessionServiceTransportTests.cs`

Regression focus:
- cursor/application keypad toggles reflected in encoded sequences
- bracketed paste wrapping when mode toggles
- correct fallback when endpoint sink is absent
- no duplicated input writes when both endpoint and transport are present

## Code Examples

### Mode-aware key encoding (test-style)

```csharp
using Avalonia.Input;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;

TerminalModeState mode = new(
    CursorVisible: true,
    ApplicationCursorKeys: true,
    ApplicationKeypad: false,
    AlternateScreen: false,
    BracketedPaste: false);

bool encoded = TerminalKeySequenceEncoder.TryEncode(
    key: Key.Up,
    modifiers: KeyModifiers.None,
    modeState: mode,
    out string sequence);

Assert.True(encoded);
Assert.Equal("\u001bOA", sequence); // app-cursor mode
```

### Input adapter fallback path

```csharp
DefaultTerminalInputAdapter adapter = new();
bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor);

if (handled)
{
    // Key was either sent to endpoint sink or encoded and forwarded to transport.
}
```

### Bracketed paste mode interaction

```csharp
// VT app enables bracketed paste: CSI ? 2004 h
vtProcessor.Process("\u001b[?2004h"u8.ToArray());
TerminalModeState state = sessionService.ModeSource!.ModeState;
Assert.True(state.BracketedPaste);
```
