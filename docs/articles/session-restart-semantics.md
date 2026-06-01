---
title: Session Restart Semantics
---

# Session Restart Semantics

This article documents how terminal state should behave when a host restarts a session in the same terminal control. It compares Ghostty, xterm.js, Windows Terminal, and RoyalTerminal because the visible behavior is not just "clear the screen". A restart has to account for primary and alternate buffers, parser state, terminal modes, keyboard and mouse protocols, cursor state, scrollback, graphics, and host-owned transport lifetime.

RoyalTerminal uses the term "session restart" for a host-level operation:

1. Stop or replace the active transport.
2. Reset terminal processor state so the next process starts from defaults.
3. Optionally preserve previous primary-buffer history.
4. Start a new transport in the same `TerminalControl`.

Reference terminal projects do not all expose that exact host-level API. Their relevant behavior comes from the same lower-level operations RoyalTerminal must compose: full reset, alternate-screen entry and exit, scrollback clear, viewport clear, input-mode reset, and renderer state refresh.

## Reference Sources

The implementation comparison is based on these reference code paths:

| Project | Relevant source |
| --- | --- |
| Ghostty | [`Terminal.fullReset`](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/Terminal.zig), [`ModeState.reset`](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/modes.zig), [`Screen.reset`](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/Screen.zig), [`StreamHandler.fullReset`](https://github.com/ghostty-org/ghostty/blob/main/src/termio/stream_handler.zig) |
| xterm.js | [`CoreTerminal.reset`](https://github.com/xtermjs/xterm.js/blob/master/src/common/CoreTerminal.ts), [`InputHandler.fullReset`](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts), [`InputHandler` private mode set/reset](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts), [`BufferSet.activateNormalBuffer`](https://github.com/xtermjs/xterm.js/blob/master/src/common/buffer/BufferSet.ts) |
| Windows Terminal | [`AdaptDispatch::HardReset`](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp), [`AdaptDispatch::_SetAlternateScreenBufferMode`](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp), [`Terminal::UseAlternateScreenBuffer`](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalCore/TerminalApi.cpp), [`Terminal::UseMainScreenBuffer`](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalCore/TerminalApi.cpp), [`ControlCore::ClearBuffer`](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalControl/ControlCore.cpp) |
| RoyalTerminal | [`TerminalControl.PrepareTerminalForSessionStart`](https://github.com/royalapplications/RoyalTerminal/blob/main/src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs), [`BasicVtProcessor.PrepareForNewSession`](https://github.com/royalapplications/RoyalTerminal/blob/main/src/RoyalTerminal.Terminal.Vt.Managed/Terminal/BasicVtProcessor.cs), [`GhosttyVtProcessor.PrepareForNewSession`](https://github.com/royalapplications/RoyalTerminal/blob/main/src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessor.cs), [`TerminalScreen.MoveViewportToScrollbackAndClear`](https://github.com/royalapplications/RoyalTerminal/blob/main/src/RoyalTerminal.Terminal/Rendering/TerminalCell.cs) |

## Core Invariants

All compared implementations follow the same conceptual model:

| Invariant | Why it matters |
| --- | --- |
| The primary buffer owns persistent shell history. | Shell prompts and command output belong here and can safely survive a session restart when the host opts into preservation. |
| The alternate buffer is app-owned and transient. | Full-screen applications such as `mc`, `btop`, `vim`, `less`, and terminal UIs write here. If the app is killed or the session is restarted before it sends its exit sequence, the terminal must still return to primary state. |
| Private modes are process-visible state. | Cursor keys, keypad, bracketed paste, mouse protocols, focus events, synchronized output, and alternate-screen state must not leak into the next process. |
| Parser state must be reset. | A restart must not resume in the middle of CSI, OSC, DCS, UTF-8, or similar parser payload state. |
| Scrollback clear and restart are different operations. | `CSI 3 J` clears scrollback. A preserved restart keeps existing history and moves the current primary viewport into history before clearing the live viewport. |
| Cursor visibility is viewport-relative. | Rendering must decide whether the cursor is visible by comparing the cursor's absolute row against the visible viewport, not by requiring the viewport to be at the live bottom. |

The important user-facing result is that restarting a session while `mc` or `btop` is still active should not preserve the application screen as shell history. The terminal should discard the alternate-screen UI, restore the primary shell buffer, then prepare the new session from that primary state.

## Ghostty Behavior

Ghostty's terminal reset path is centered around `Terminal.fullReset`.

### Full Reset

`Terminal.fullReset` performs these state transitions:

| State area | Reset behavior |
| --- | --- |
| Active screen | Switches to the primary screen. |
| Alternate screen | Removes the alternate screen object. |
| Primary screen contents | Calls `Screen.reset`, clearing the active pages and returning the cursor to the top-left page pin. |
| Modes | Calls `modes.reset`, restoring default values and clearing saved mode state. |
| Dirty flags | Reinitializes flags and marks the screen for a full redraw. |
| Tab stops | Restores regular tab stops using the terminal tab interval. |
| Previous character | Clears previous-character state used by parser/print behavior. |
| Working directory and title | Clears retained working directory and title strings. |
| Status display | Returns to the main display. |
| Scrolling region | Restores top, bottom, left, and right margins to the whole terminal. |

`Screen.reset` also resets screen-local state:

| Screen state | Reset behavior |
| --- | --- |
| Cursor | Reinitializes cursor state at the new page origin. |
| Saved cursor | Clears saved cursor state. |
| Character sets | Restores default charset state. |
| Kitty keyboard | Clears Kitty keyboard flags and stack. |
| Protected mode | Returns protection mode to off. |
| Selection | Clears selection. |
| Kitty graphics | Deletes stored graphics when the feature is enabled. |
| Semantic prompt | Disables semantic prompt state. |

`ModeState.reset` returns mode values to configured defaults and clears the saved-mode map used by save/restore mode sequences.

### Mode Defaults

Ghostty's mode table defines the default state. The important defaults for restart behavior are:

| Mode | Default |
| --- | --- |
| ANSI insert mode | Off |
| ANSI send/receive mode | On |
| ANSI linefeed/newline mode | Off |
| DEC application cursor keys | Off |
| DEC origin mode | Off |
| DEC wraparound | On |
| DEC cursor visible | On |
| DEC alternate screen modes `47`, `1047`, `1049` | Off |
| DEC application keypad | Off |
| DEC backarrow key mode | Off |
| Mouse tracking and encodings | Off, except alternate scroll mode defaults on |
| Bracketed paste | Off |
| Synchronized output | Off |
| Color scheme reports | Off |
| In-band size reports | Off |
| Ignore keypad with numlock | On |
| Alt escape prefix | On |

### Termio Side Effects

Ghostty's `StreamHandler.fullReset` wraps `terminal.fullReset` and also:

| State area | Reset behavior |
| --- | --- |
| Mouse cursor shape | Returns mouse shape to text. |
| Color scheme report | Emits a color-scheme report because reset can affect palette state. |
| Progress reporting | Removes progress state. |

These are not VT grid cells, but they are still visible integration state. A host that builds on Ghostty must not preserve stale values from the old app.

### Alternate Screen

Ghostty treats alternate screen as application-owned state. The reset path switches back to the primary screen and removes alternate state. That is the behavior RoyalTerminal follows for preserved restarts.

For explicit clear behavior, Ghostty supports:

| Sequence | Behavior |
| --- | --- |
| `CSI 3 J` | Erase scrollback/history. |
| `CSI 22 J` | Scroll-complete extension: move the current active viewport into scrollback and clear the live screen. |

RoyalTerminal uses `CSI 22 J` semantics as the native primitive for preserving a completed session's visible primary viewport into history.

## xterm.js Behavior

xterm.js separates terminal reset into service resets and buffer activation.

### Full Reset

`InputHandler.fullReset` resets the parser and fires a reset request. `CoreTerminal.reset` then resets:

| Service | Reset behavior |
| --- | --- |
| Input handler | Resets current attributes and erase attributes to defaults. |
| Buffer service | Resets the normal and alternate buffer set. |
| Charset service | Restores charset designations. |
| Core service | Resets core DEC private modes and cursor initialization state. |
| Mouse state service | Resets mouse protocol and encoding state. |

This mirrors the same separation RoyalTerminal uses: parser and modes belong to the processor, while scrolling and screen rows belong to the screen model.

### Alternate Screen

xterm.js implements alternate screen through `BufferSet.activateAltBuffer` and `BufferSet.activateNormalBuffer`.

Entering alternate screen:

| State area | Behavior |
| --- | --- |
| Alternate buffer | Filled to viewport size with erase attributes. |
| Cursor position | Alternate `x` and `y` copy the normal buffer position. |
| Kitty keyboard | Main flags are saved and alternate flags are restored when Kitty keyboard extension is enabled. |
| Active buffer | Switches active buffer to alternate. |
| Scrollbar | Requests scrollbar sync. |

Leaving alternate screen:

| State area | Behavior |
| --- | --- |
| Normal buffer | Becomes active. |
| Cursor position | Normal `x` and `y` copy the alternate position, then `1049` restore cursor can apply saved cursor state. |
| Alternate buffer | Markers are cleared and the buffer is cleared. |
| Kitty keyboard | Alternate flags are saved and main flags are restored. |
| Refresh | Rows are refreshed and scrollbar is synchronized. |

The important part for restart is that the alternate buffer is not a history store. It is cleared when normal buffer is reactivated.

### Private Mode Reset

xterm.js explicitly resets private modes that affect process interaction:

| Mode area | Reset behavior |
| --- | --- |
| Application cursor keys | Off. |
| Origin mode | Off and cursor home is reset. |
| Wraparound | Can be reset and queried. |
| Reverse wraparound | Off. |
| Application keypad | Off. |
| Mouse protocols | Active protocol reset to none. |
| Mouse encodings | Encoding reset to default. |
| Focus events | Off. |
| Cursor visibility | `DECTCEM` controls hidden/visible state. |
| Alternate buffer modes | Return to normal buffer. |
| Bracketed paste | Off. |
| Synchronized output | Off. |

RoyalTerminal's managed and native restart paths reset the same mode categories so input encoding and paste behavior do not leak from one process into the next.

### Scrollback and Cursor Visibility

xterm.js uses absolute buffer coordinates for cursor visibility:

1. Cursor absolute row is `ybase + y`.
2. Viewport top is `ydisp`.
3. Cursor is visible when `absoluteY - ydisp` is inside the viewport rows.

This is why a cursor can be rendered while viewing preserved restart history when the cursor row is actually in the visible row range. RoyalTerminal follows this model in its renderer synchronization.

## Windows Terminal Behavior

Windows Terminal splits behavior across the parser dispatch layer and terminal core.

### Hard Reset

`AdaptDispatch::HardReset` performs a reset to initial state. Key behavior:

| State area | Reset behavior |
| --- | --- |
| Parser/state machine | Reset through the state machine before dispatch. |
| Active screen contents | Optional erase of display and scrollback when reset is invoked with erase. |
| SGR and character sets | Soft reset restores character set designations and attributes. |
| Code page and C1 controls | Code page reset and C1 parsing/sending disabled. |
| Render settings | Restored to default startup settings. |
| Cursor | Moves to home when erasing; otherwise cursor position is preserved for the next shell prompt. |
| Linefeed mode | Resets linefeed if the input mode owns it. |
| Input modes | `TerminalInput.ResetInputModes` returns input modes to defaults. |
| Bracketed paste | Off. |
| Cursor blink | On. |
| Tab stops | Clears current tab stops and sets every eight columns. |
| Soft font | Clears renderer soft font and font buffer. |
| Internal modes | Restores internal modes to initial state. |
| Macro buffer | Clears and releases macro state. |
| ConPTY integration | Injects a reset marker so ConPTY can re-enable modes it requires. |

Windows Terminal distinguishes a reset that erases buffers from a reset that preserves contents. That distinction maps to RoyalTerminal's default restart versus preserve-history restart.

### Alternate Screen

`AdaptDispatch::_SetAlternateScreenBufferMode` maps private mode `1049` to core buffer operations.

Entering alternate screen:

| State area | Behavior |
| --- | --- |
| Cursor | Save cursor state. |
| Alternate buffer | Create a viewport-sized alternate buffer. |
| Cursor style and visibility | Copy cursor size, type, visibility, and blink into the alternate buffer. |
| Cursor position | Convert main-buffer position to viewport-relative alternate-buffer position. |
| Input | Notify `TerminalInput` that the alternate buffer is active. |
| Selection and links | Clear selection and update URL detection. |
| Scrollbars and redraw | Notify scroll event and trigger redraw. |

Leaving alternate screen:

| State area | Behavior |
| --- | --- |
| Active buffer | Main buffer becomes active. |
| Alternate buffer | Destroyed after cursor data is read. |
| Deferred resize | Applied before cursor state is copied back. |
| Cursor style and visibility | Main cursor adopts current alternate cursor style, visibility, and blink. |
| Cursor position | Alternate viewport-relative position is converted back to main-buffer coordinates. |
| Input | Notify `TerminalInput` that main screen is active. |
| Scrollbars and redraw | Notify scroll event and redraw active buffer. |

This matches xterm's high-level model: alternate buffer is discarded when leaving it, and main buffer becomes the durable screen.

### Clear Buffer

Windows Terminal's `ControlCore::ClearBuffer` separates three operations:

| Clear type | Behavior |
| --- | --- |
| Scrollback | Emits `CSI 3 J`. |
| Screen | Keeps the cursor row, erases above and below it, and moves the cursor row to the top. |
| All | Clears scrollback plus screen contents while preserving the cursor row behavior. |

RoyalTerminal exposes separate `ClearScrollback()` and restart preservation operations for the same reason: user intent differs between clearing old history and restarting a session while retaining a transcript.

## RoyalTerminal Behavior

RoyalTerminal has two VT processors but one session host contract.

| Component | Responsibility |
| --- | --- |
| `TerminalControl` | Owns Avalonia host state, renderer, session service, scroll data, selection, and transport start/stop orchestration. |
| `TerminalSessionService` | Connects a transport or endpoint to the active processor, routes data, and exposes mode/input state. |
| `BasicVtProcessor` | Fully managed parser and screen updater. |
| `GhosttyVtProcessor` | Native Ghostty-backed parser and screen updater. |
| `TerminalScreen` | Scrollback-aware row model, primary/alternate backing buffers, raster graphics, Kitty graphics, viewport position, and dirty state. |

### Restart Entry Point

`TerminalControl.StartSessionAsync(options, preserveScrollback)` calls `PrepareTerminalForSessionStart` before the new transport starts.

The control first:

1. Flushes pending transport output.
2. Clears selection.
3. Resets cursor blink phase.
4. Records previous row and scroll metrics.
5. Locks the screen model.

Then it chooses one of three paths:

| Processor capability | `preserveScrollback` | Behavior |
| --- | --- | --- |
| Processor implements `ITerminalSessionHistoryController` | `true` or `false` | Delegate to `PrepareForNewSession(preserveScrollback)`. |
| Processor does not implement the history controller | `true` | Restore primary screen if alternate buffer is active, discard inactive alternate rows, move primary viewport into scrollback, clear the live viewport, then reset processor state. |
| Processor does not implement the history controller | `false` | Clear all screen state and reset processor state. |

After screen mutation, `TerminalControl` synchronizes scroll state so a preserved restart remains pinned to the live bottom. This prevents a parent scroll viewer from replaying an older top-anchored offset after the terminal extent changes.

### Managed VT Restart

`BasicVtProcessor.PrepareForNewSession` calls the internal reset path with either `ClearAll` or `PreserveScrollback`.

The managed restart reset covers:

| State area | Reset behavior |
| --- | --- |
| Cursor position | Column and row set to zero. |
| Delayed wrap | Cleared. |
| Parser state | Returns to ground state. |
| CSI parameters and markers | Cleared. |
| OSC buffer | Cleared, including discard state. |
| DCS buffer | Cleared, including discard state. |
| UTF-8 and graphics context | Last graphic codepoint and line drawing state reset. |
| Scroll region | Restored to full viewport. |
| Alternate screen | Switches to primary if active. |
| Inactive alternate buffer | Discarded. |
| Auto-wrap | Restored on. |
| Cursor visibility | Restored visible. |
| Origin mode | Off. |
| Application cursor keys | Off. |
| Application keypad | Off. |
| Backarrow key mode | Off. |
| Save cursor mode | Off. |
| Bracketed paste | Off. |
| Win32 input mode | Off. |
| Keyboard action mode | Off. |
| Send/receive mode | On. |
| Extended DEC modes | Reset to defaults, including alternate scroll, ignore keypad with numlock, and alt escape prefix. |
| Sixel display mode | Off. |
| Insert mode | Off. |
| Linefeed/newline mode | Off. |
| Cursor style | Restored to default blinking block. |
| Saved style and hyperlink state | Cleared. |
| Current hyperlink | Cleared. |
| Kitty keyboard | Main and alternate flags cleared and stacks emptied. |
| SGR attributes | Reset to defaults. |
| Tab stops | Reinitialized. |

If history is preserved, the reset then calls `TerminalScreen.MoveViewportToScrollbackAndClear`. Because the processor has already returned from alternate screen to primary, an abrupt restart from `mc` or `btop` preserves the shell's primary viewport, not the transient full-screen application buffer.

### Managed Screen Preservation

`TerminalScreen.MoveViewportToScrollbackAndClear` does this on the primary buffer:

1. Discards transient resize rows.
2. Resets scroll offset to the live bottom.
3. Counts non-empty viewport rows.
4. Appends enough blank rows to move those rows into scrollback.
5. Trims scrollback to the configured limit.
6. Clears the live viewport rows.
7. Clears raster graphics in the live viewport rectangle.
8. Clears Kitty graphics for the new live session.
9. Marks the screen dirty.

The preserved history keeps `TerminalRow` and `TerminalCell` instances, so text, colors, attributes, underline style, decorations, and hyperlink ids remain available for copied rows.

If the screen is still in alternate-buffer mode, the low-level screen primitive only clears the alternate viewport. The processor and control restart paths therefore switch back to primary before using this primitive for preserved restart.

### Native Ghostty Restart

`GhosttyVtProcessor.PrepareForNewSession(true)` cannot call Ghostty's native full reset directly because a full reset clears the preserved history. Instead it writes a precomputed VT sequence that composes Ghostty's existing parser behavior:

| Step | Sequence category | Purpose |
| --- | --- | --- |
| 1 | `DECRST 1049`, `1047`, `47` | Exit alternate-screen modes and return to the primary buffer. |
| 2 | `CSI 22 J` | Move the active primary viewport into scrollback and clear the live viewport. |
| 3 | ANSI reset modes | Reset keyboard action, insert, and linefeed/newline modes; restore send/receive mode. |
| 4 | DEC private reset modes | Reset application cursor keys, origin, column, reverse video, mouse, focus, alternate-screen, bracketed paste, synchronized output, color report, in-band report, and Win32 input modes. |
| 5 | DEC private default-on modes | Restore wraparound, cursor visible, alternate scroll, ignore keypad with numlock, and alt escape prefix. |
| 6 | Kitty keyboard reset | Pop/reset Kitty keyboard state to disabled. |
| 7 | Margins and cursor | Reset scroll region and move cursor home. |
| 8 | Cursor style, SGR, charsets | Restore default cursor style, default attributes, G0/G1 ASCII charsets, and G0 invocation. |

After the native parser processes the sequence, RoyalTerminal reapplies optional native features, theme colors, terminal effects, mouse encoder state, and sixel overlay state. It then refreshes screen state and raises mode-change notifications if needed.

### Non-Preserving Restart

When `preserveScrollback` is `false`, RoyalTerminal clears the terminal instead of moving current rows into history:

| Processor | Behavior |
| --- | --- |
| Managed | `BasicVtProcessor` resets state and calls `TerminalScreen.ClearAll`. |
| Native Ghostty | `GhosttyVtProcessor` calls its `Reset` path, which maps to native reset behavior and refreshes state. |
| Fallback processor | `TerminalControl` calls `TerminalScreen.ClearAll` and then `IVtProcessor.Reset`. |

This is the correct behavior for hosts that expect a clean terminal every time a connection starts.

## State Comparison Matrix

| State | Ghostty | xterm.js | Windows Terminal | RoyalTerminal |
| --- | --- | --- | --- | --- |
| Parser state | Full reset clears parser-side state through stream/terminal reset. | Parser reset fires request reset; input handler reset clears attributes. | State machine reset plus dispatch reset. | Managed clears parser buffers and markers; native uses Ghostty parser reset sequence or native reset. |
| Primary buffer | Reset clears active primary; scroll-complete can move viewport into scrollback. | Normal buffer reset or clear depending operation. | Main buffer persists across alternate screen; hard reset can erase. | Preserved restart keeps previous history and moves primary viewport into scrollback; clean restart clears all. |
| Alternate buffer | Full reset switches primary and removes alternate. | Normal activation clears alternate buffer. | Main activation destroys alternate buffer. | Managed/native restart exits alternate first; fallback control restores primary first. |
| Scrollback | `CSI 3 J` clears history; `CSI 22 J` preserves viewport into history. | ED 3 clears scrollback; clear can keep prompt row. | `CSI 3 J` clears scrollback; clear types distinguish scrollback/screen/all. | `ClearScrollback` clears history only; preserved restart retains history and clears live viewport. |
| Cursor position | Reset homes cursor; scroll-complete homes live viewport in RoyalTerminal native sequence. | Cursor is buffer-relative; 1049 restore applies saved cursor. | Cursor saved/restored for alt screen; hard reset homes when erasing. | Restart homes new live session; renderer uses absolute row visibility. |
| Cursor visibility and style | Mode defaults restore visible cursor and reset cursor state. | `DECTCEM` and cursor options reset through core/input state. | Cursor visibility/blink copied through alt transition and reset by hard reset. | Managed resets visible and default style; native sequence resets visible and cursor style. |
| Keyboard modes | Mode state reset restores defaults. | Application cursor/keypad reset through core service. | `TerminalInput.ResetInputModes` restores defaults. | Managed/native reset application cursor, keypad, backarrow, Win32 input, and Kitty keyboard state. |
| Mouse modes | Mode defaults reset tracking and encodings. | Mouse state service reset clears protocol/encoding. | Terminal input modes reset mouse protocols and encodings. | Managed/native reset mouse tracking, encodings, focus, and pressed button state. |
| Bracketed paste | Default off. | Reset off. | System mode reset off. | Managed/native reset off. |
| Focus events | Default off. | Reset off. | Input mode reset off, with ConPTY reinjection as needed. | Managed/native reset off. |
| Synchronized output | Default off. | Reset off. | Render setting reset and synchronized output notification. | Managed/native reset off. |
| Color and SGR attributes | Screen/cursor styles reset; colors may be restored/report updated. | Input handler restores default attributes. | Soft reset restores rendition, render settings reset. | Managed resets SGR; native writes SGR reset and reapplies host theme. |
| Character sets | Screen reset clears charset. | Charset service reset. | Soft reset resets character set designations. | Managed resets G0/G1 and line drawing; native writes G0/G1 ASCII and shift-in. |
| Tab stops | Reset every tab interval. | Buffer/core reset restores defaults. | Hard reset sets every eight columns. | Managed reinitializes tabs; native sequence leaves Ghostty parser defaults or native reset path handles clean restart. |
| Graphics | Kitty graphics reset on screen reset. | Not the same native graphics model. | Sixel parser soft reset and soft font clear. | Managed clears live Kitty/raster graphics for restart; native syncs sixel overlay and clears live graphics through parser/screen refresh. |
| Selection | Cleared on reset or screen switch. | Selection service tracks active buffer. | Selection cleared when switching buffers. | `TerminalControl` clears selection before restart. |
| Scroll viewport | Scrollbar reflects active buffer. | `ydisp` tracks viewport; cursor visibility is absolute row minus `ydisp`. | Scroll event notified after buffer switch. | Scroll data is resynchronized and pinned to live bottom after preserved restart. |

## Apps Such As `mc` And `btop`

Full-screen terminal applications normally enter alternate screen with `CSI ? 1049 h` and exit with `CSI ? 1049 l`. If the process exits cleanly, the app sends the exit sequence and the terminal returns to the primary buffer before the next prompt.

A host-level restart can interrupt the app before it sends the exit sequence. Correct terminal behavior still must restore the primary buffer:

1. Detect or force alternate-screen exit.
2. Discard the alternate app-owned buffer.
3. Restore primary shell content.
4. Preserve primary content into history if requested.
5. Reset modes that the app may have enabled.
6. Start the next process with a blank live viewport.

RoyalTerminal covers this in both processor paths:

| Path | Behavior |
| --- | --- |
| Managed | `BasicVtProcessor.ResetInternal` switches to primary before `MoveViewportToScrollbackAndClear`. |
| Native Ghostty | The preserved restart sequence sends alternate-screen resets before `CSI 22 J`. |
| Fallback control path | `TerminalControl` switches `TerminalScreen` to primary before preserving history when the processor lacks `ITerminalSessionHistoryController`. |

This means a restart from `mc`, `btop`, or a similar TUI preserves the shell transcript that launched the app, not the application's alternate-screen UI.

## What RoyalTerminal Preserves

With `preserveScrollback: true`, RoyalTerminal preserves:

| State | Preserved? | Notes |
| --- | --- | --- |
| Existing primary scrollback rows | Yes | Kept up to `ScrollbackLimit`. |
| Current primary viewport text | Yes | Moved into scrollback before the new live viewport is cleared. |
| Cell styling in preserved rows | Yes | Text, foreground, background, attributes, underline style, decorations, and hyperlink ids remain in row cells. |
| Alternate-screen app UI | No | It is transient app-owned state. |
| Live viewport for the new process | No | Cleared to blank rows. |
| Host theme and renderer settings | Yes | These are control configuration, not process state. |
| Transport process | No | The old transport is stopped/replaced; restart does not resurrect the app. |
| Parser state and modes | No | Reset so the new process starts from terminal defaults. |
| Selection | No | Cleared before restart. |
| Mouse pressed-button state | No | Reset to avoid stale drag/button reporting. |
| Cursor blink phase | No | Reset so the cursor starts visible according to current cursor settings. |

## Validation Contracts

The restart behavior is covered by focused tests:

| Test area | Coverage |
| --- | --- |
| `TerminalSessionHistoryTests` | Screen primitives, managed `CSI 3 J`, managed `CSI 22 J`, managed preserved restart from alternate-screen apps, and managed mode reset. |
| `TerminalControlTests` | `StartSessionAsync(..., preserveScrollback)`, scroll pinning after restart, fallback primary-buffer restore, explicit scrollback clear, and cursor visibility while scrolled. |
| `GhosttyVtProcessorTests` | Native scrollback clear, native preserved restart, and native process-visible mode reset. |
| Demo ViewModel/controller tests | Restart Session, Preserve History, and Clear History command routing through ViewModels and interactions. |

These tests encode the same reference decision documented above: restart must reset process-owned terminal state, restore primary-buffer ownership, and preserve only durable primary history when requested.
