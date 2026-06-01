---
title: Session History And Scrollback
---

# Session History And Scrollback

RoyalTerminal can keep a terminal control alive after a session exits and start the next session in the same control without discarding the previous history. This is opt-in so hosts that expect a clean terminal on every connection keep the existing behavior.

```csharp
terminal.PreserveScrollbackOnSessionStart = true;
await terminal.StartSessionAsync(options);

await terminal.StartSessionAsync(nextOptions, preserveScrollback: true);

terminal.ClearScrollback();
```

## Public API

`TerminalControl.PreserveScrollbackOnSessionStart` controls the default behavior of `StartSessionAsync(options)`. It defaults to `false`.

`TerminalControl.StartSessionAsync(options, preserveScrollback: true)` starts a specific session with history preservation regardless of the property value. Typed helpers such as `StartPipeAsync`, `StartSshAsync`, `StartRawTcpAsync`, `StartTelnetAsync`, and `StartSerialAsync` also have `preserveScrollback` overloads.

`TerminalControl.ClearScrollback()` clears history explicitly while keeping the active viewport intact. Use it for "Clear History" UI commands, session reset actions, or host policies that retain a live prompt but discard older output.

## Behavior

When `preserveScrollback` is `false`, RoyalTerminal resets the processor and clears all terminal rows before the new transport starts.

When `preserveScrollback` is `true`, RoyalTerminal resets parser, keyboard, mouse, alternate-screen, style, and mode state for the new session, moves any non-empty active viewport rows into scrollback, clears the active viewport, and starts the new transport at a blank prompt area. Existing scrollback rows remain available.

`ClearScrollback()` is scrollback-only. It does not clear the active viewport and does not stop the running transport.

## Managed VT Implementation

The managed VT path uses `TerminalScreen.ClearScrollback()` for explicit history clear and `TerminalScreen.MoveViewportToScrollbackAndClear()` for session preservation. The screen model keeps `TerminalRow` and `TerminalCell` instances, so text, foreground/background colors, attributes, underline state, decorations, and hyperlink ids are preserved for rows that remain in history.

`BasicVtProcessor` also handles the relevant erase-display sequences:

| Sequence | Behavior |
| --- | --- |
| `CSI 3 J` | Clears scrollback only. |
| `CSI 22 J` | Moves the active viewport into scrollback and clears the active viewport. |

The `CSI 22 J` behavior follows Ghostty's "scroll complete" extension so managed and native processors have the same session-preservation primitive.

## Ghostty VT Implementation

The native Ghostty path wires the same public API through Ghostty's parser instead of adding a separate native binding. Current `libghostty-vt` exposes terminal reset and VT write APIs, but not a dedicated C API for scrollback-only clear. RoyalTerminal therefore writes the control sequences Ghostty already implements:

| API | Native sequence |
| --- | --- |
| `ClearScrollback()` | `CSI 3 J` |
| `PrepareForNewSession(true)` | exit alternate screen, `CSI 22 J`, reset common modes, reset SGR, reset charset |

The session-preparation path reapplies optional Ghostty features, theme colors, callbacks, mouse encoder state, and sixel overlay state after the native sequence has been processed.

## Reference Decision

RoyalTerminal follows `CSI 3 J` for explicit scrollback clear and Ghostty's `CSI 22 J` semantics for preserving the active viewport into scrollback before a new session starts.

See [Session Restart Semantics](/articles/session-restart-semantics) for the detailed Ghostty, xterm.js, Windows Terminal, and RoyalTerminal comparison, including alternate-screen app restart behavior and process-visible mode reset state.

## Demo App

The sample demo exposes the feature in the toolbar:

- `Preserve History` toggles `TerminalControl.PreserveScrollbackOnSessionStart`.
- `Restart Session` restarts the active standalone tab and uses the toggle value.
- `Clear History` calls `TerminalControl.ClearScrollback()` for the active standalone tab.

The demo keeps the interaction in the ViewModel through ReactiveUI commands and routes the concrete terminal operation through `MainWindowController`.

## Tests

Focused coverage lives in:

- `TerminalSessionHistoryTests` for screen primitives and managed VT sequences.
- `TerminalControlTests` for `StartSessionAsync(..., preserveScrollback)` and `ClearScrollback()`.
- `GhosttyVtProcessorTests` for native scrollback clear and preservation when `libghostty-vt` is available.
- `MainWindowViewModelFlowTests` and `MainWindowControllerModeStartupTests` for demo command wiring and behavior propagation.
