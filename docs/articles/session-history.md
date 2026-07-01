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
terminal.ClearHistory();
```

## Public API

`TerminalControl.PreserveScrollbackOnSessionStart` controls the default behavior of `StartSessionAsync(options)`. It defaults to `false`.

`TerminalControl.StartSessionAsync(options, preserveScrollback: true)` starts a specific session with history preservation regardless of the property value. Typed helpers such as `StartPipeAsync`, `StartSshAsync`, `StartRawTcpAsync`, `StartTelnetAsync`, and `StartSerialAsync` also have `preserveScrollback` overloads.

`TerminalControl.ClearScrollback()` clears scrollback explicitly while keeping the active viewport intact. Use it when the host wants to discard off-screen history without changing the current visible terminal contents.

`TerminalControl.ClearHistory()` clears scrollback and moves the active cursor row to the first viewport row. It is useful for host UI commands that should remove previous command output rather than only dropping off-screen history.

`TerminalControl.RequestPromptRedraw()` sends form feed (`Ctrl+L`) to the active endpoint through the prompt-control input path. Use it after host-side clear-history commands when an active interactive shell or prompt integration needs to repaint its prompt and synchronize its internal cursor position.

## Behavior

When `preserveScrollback` is `false`, RoyalTerminal resets the processor and clears all terminal rows before the new transport starts.

When `preserveScrollback` is `true`, RoyalTerminal resets parser, keyboard, mouse, alternate-screen, style, and mode state for the new session. If the session was on the primary screen, any non-empty primary viewport rows are moved into scrollback and the active viewport is cleared for the new transport. If the session was interrupted while an alternate-screen application such as `mc` or `btop` was active, RoyalTerminal first returns to the primary screen, discards the transient application screen, restores the primary cursor, and keeps the restored primary prompt area visible. Existing scrollback rows remain available in both cases.

`ClearScrollback()` is scrollback-only. It does not clear the active viewport and does not stop the running transport.

`ClearHistory()` is viewport-aware. It keeps the current cursor row as the new first row, drops rows above it, blanks rows below it, and resets the scroll position. The terminal process is not automatically notified. Hosts that expose this as an interactive shell command should call `RequestPromptRedraw()` after the clear only when a live shell/session input path exists, so shells that maintain their own prompt/cursor state can repaint cleanly.

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
| `ClearHistory()` | reset scroll margins, `CSI 3 J`, erase below the cursor row, delete rows above the cursor row, move cursor to row 1 |
| `PrepareForNewSession(true)` from primary screen | `CSI 22 J`, reset common modes, reset SGR, reset charset |
| `PrepareForNewSession(true)` from alternate screen | exit alternate screen, reset common modes, reset SGR, reset charset, restore the primary cursor |

The session-preparation path reapplies optional Ghostty features, theme colors, callbacks, mouse encoder state, and sixel overlay state after the native sequence has been processed.

## Reference Decision

RoyalTerminal follows `CSI 3 J` for explicit scrollback clear. For primary-screen preserved restarts it follows Ghostty's `CSI 22 J` semantics by preserving the active primary viewport into scrollback before a new session starts. For interrupted alternate-screen restarts it follows `1049l`-style behavior by returning to primary, restoring the saved primary cursor, and leaving that primary prompt area visible.

See [Session Restart Semantics](/articles/session-restart-semantics) for the detailed Ghostty, xterm.js, Windows Terminal, and RoyalTerminal comparison, including alternate-screen app restart behavior and process-visible mode reset state.

## Shared Shell

The reusable app shell exposes the feature in the Session menu:

- `Preserve History` toggles `TerminalControl.PreserveScrollbackOnSessionStart`.
- `Restart Session` restarts the active standalone tab and uses the toggle value.
- `Clear History` calls `TerminalControl.ClearHistory()`, scrolls to the live bottom, and then calls `RequestPromptRedraw()` when the tab has an active session so interactive shells repaint their prompt after the host-side clear.

The shared shell keeps the interaction in the ViewModel through ReactiveUI commands and routes the concrete terminal operation through `MainWindowController`.

## Tests

Focused coverage lives in:

- `TerminalSessionHistoryTests` for screen primitives and managed VT sequences.
- `TerminalControlTests` for `StartSessionAsync(..., preserveScrollback)`, `ClearScrollback()`, `ClearHistory()`, and prompt redraw requests.
- `GhosttyVtProcessorTests` for native scrollback clear and preservation when `libghostty-vt` is available.
- `MainWindowViewModelFlowTests` and `MainWindowControllerModeStartupTests` for shared shell command wiring and behavior propagation.
