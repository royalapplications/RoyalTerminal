---
title: Session Restart Reference Analysis
---

# Session Restart and History Reference Analysis

This document reviews RoyalTerminal session restart, clear scrollback, and clear history behavior against Ghostty, xterm.js, and Windows Terminal. It also records the implementation findings and the resulting behavior decisions.

## Scope

RoyalTerminal exposes a preserved session restart path, explicit scrollback/history commands, demo-app controls, tests, and documentation. The terminal-facing behavior is concentrated in these components:

- `TerminalControl.StartSessionAsync(..., preserveScrollback)` and `PrepareTerminalForSessionStart(...)`
- `ITerminalSessionHistoryController`
- `BasicVtProcessor.PrepareForNewSession`, `ClearScrollback`, and `ClearVisibleHistory`
- `GhosttyVtProcessor.PrepareForNewSession`, `ClearScrollback`, and `ClearVisibleHistory`
- `TerminalScreen.ClearScrollback`, `ClearVisibleHistory`, and `MoveViewportToScrollbackAndClear`

The review treats session restart as a terminal-buffer operation, not as process checkpointing. Restarting a transport creates a new process. Terminal state that is inside the terminated application is not recoverable unless the application itself persists it or a separate multiplexer/session manager keeps the process alive.

## References Reviewed

### Ghostty

Relevant source:

- `external/ghostty/src/terminal/Terminal.zig`
- `external/ghostty/src/terminal/Screen.zig`
- `external/ghostty/src/terminal/PageList.zig`
- `external/ghostty/src/terminal/modes.zig`
- `external/ghostty/src/terminal/stream_terminal.zig`

Observed behavior:

- Erase display mode `scrollback` maps to `screens.active.eraseHistory(null)`, which physically removes history rows.
- Erase display mode `scroll_complete` calls `screens.active.scrollClear()`, then reloads the cursor and clears image state for the active screen.
- A complete display erase may first run `scrollClear()` when Ghostty detects prompt-like semantic rows on the primary screen, then clears active rows.
- Alternate-screen mode `1049` saves the cursor on entry. Disabling `1049` switches to the primary screen and restores that saved cursor.
- Full reset switches back to the primary screen, removes alternate-screen state, resets the screen, and resets modes.
- Mode reset is explicit. Ghostty defaults wraparound, cursor visibility, alternate-scroll mouse behavior, ignore-keypad-with-numlock, and alternate escape prefix to enabled, while application cursor, application keypad, bracketed paste, mouse reporting, origin mode, reverse video, and alternate screen modes reset to disabled.

RoyalTerminal alignment:

- The Ghostty-backed restart path uses the native terminal as the authority for preserved restart.
- Preserved restart resets process-visible VT modes to Ghostty defaults and then reapplies RoyalTerminal-owned optional native features such as theme and rendering effects.
- Preserved restart uses `CSI 22J` only when the primary screen is active. When alternate screen is active, it first exits alternate screen, captures the restored primary cursor, resets process-visible state, and keeps the restored primary prompt area visible.
- Scrollback erase uses `CSI 3J`, matching Ghostty's erase-history path.
- Clear-history now uses row-preserving VT editing commands instead of reconstructing text. This keeps the native row state intact in the same spirit as Ghostty's physical row operations.
- Prompt-aware host commands request a shell redraw after emulator-side clear. Ghostty does this with form feed when semantic prompt detection says the cursor is at a prompt.

### xterm.js

Relevant source:

- xterm.js `src/headless/Terminal.ts`
- xterm.js `src/common/InputHandler.ts`

Observed behavior:

- Public `Terminal.clear()` keeps the active cursor line as the new first buffer line, clears markers, resets `ydisp`, `ybase`, and `y` to zero, then fills the viewport with blank lines.
- `CSI 3J` erases scrollback only. It trims historical rows before the viewport and clamps `ybase` and `ydisp` to zero.
- Alternate-screen activation/deactivation swaps between normal and alternate buffers, updates kitty keyboard flag storage, saves/restores cursor state for `1049`, refreshes the viewport, and synchronizes the scrollbar.
- Soft reset clears many process-visible modes, restores scroll margins, resets character sets, shows the cursor, and resets core service state.

RoyalTerminal alignment:

- `TerminalScreen.ClearVisibleHistory` follows the public xterm.js clear behavior: preserve the cursor row, make it row zero, drop previous rows, and blank the rest of the viewport.
- `TerminalScreen.ClearScrollback` follows `CSI 3J`: drop only historical rows and keep the visible viewport.
- Managed primary-screen preserved restart uses `MoveViewportToScrollbackAndClear`, which models the "current viewport becomes preserved scrollback, active display becomes blank" behavior needed for a completed primary-buffer session.
- Managed alternate-screen restart follows xterm's `1049l` shape: return to primary, restore the saved cursor, discard inactive alternate state, and do not clear the restored primary prompt area.

### Windows Terminal

Relevant source:

- Windows Terminal `src/cascadia/TerminalControl/ControlCore.cpp`
- Windows Terminal `src/cascadia/TerminalConnection/ConptyConnection.cpp`
- Windows Terminal `src/buffer/out/textBuffer.cpp`
- Windows Terminal `src/cascadia/UnitTests_Control/ControlCoreTests.cpp`

Observed behavior:

- `ClearBuffer(Scrollback)` sends `CSI 3J`.
- `ClearBuffer(Screen)` and `ClearBuffer(All)` preserve the cursor row. They erase below the cursor row, delete rows above the cursor row, and restore the cursor column.
- `ClearBuffer(All)` combines scrollback erase with the screen-row preservation sequence.
- `ConptyConnection.ClearBuffer(true)` asks ConPTY to clear while keeping the cursor row, avoiding divergence between visible terminal state and the backing console buffer.
- `TextBuffer::ClearScrollback` physically moves retained viewport rows to the start and decommits/clears historical rows.
- The alternate buffer is represented as a separate screen object with a pointer back to the main buffer. Returning to the main buffer reactivates the main screen, updates scrollbars and input buffer state, copies cursor style/visibility, and deletes the alternate screen object.

RoyalTerminal alignment:

- Managed clear-scrollback and clear-history perform the same logical buffer operations locally.
- Ghostty-backed clear-history now follows the Windows Terminal VT sequence shape:
  - `CSI 3J` to erase scrollback.
  - `CSI <cursorRow + 2>;1H CSI J` to erase below the cursor row.
  - `CSI H CSI <cursorRow>M` to delete rows above the cursor row.
  - `CSI 1;<cursorColumn + 1>H` to restore cursor position.
- This preserves the actual native cursor row instead of copying plain text back into the terminal.

## Behavior Matrix

| Operation | Ghostty | xterm.js | Windows Terminal | RoyalTerminal |
| --- | --- | --- | --- | --- |
| Clear scrollback | `eraseHistory(null)` for `CSI 3J` | trims historical rows and resets scroll offsets | sends `CSI 3J`, viewport height remains visible height | `ClearScrollback` drops history and preserves viewport; native processors own native sync |
| Clear visible history | `scrollClear`/physical row edits in related clear paths | moves cursor line to first row and blanks viewport | preserves cursor row by erasing below and deleting rows above | managed copies the cursor `TerminalRow`; Ghostty native now shifts the existing row |
| Prompt redraw after host clear | sends form feed when at a semantic prompt | host API is emulator-only | ConPTY clear stays coherent with the backing console | demo calls `RequestPromptRedraw()` after host-side clear-history |
| Preserved restart from primary | VT state can be reset while scrollback is retained by native operations; `CSI 22J` scroll-completes active primary screen | reset/clear APIs affect buffer, not process resurrection | terminal buffer can be cleared/preserved, ConPTY process state is separate | primary viewport is moved into scrollback, active screen is cleared, process-visible modes are reset |
| Preserved restart from alternate | `1049l` returns to primary and restores saved cursor | `1049l` activates normal buffer and restores cursor | main buffer is reactivated and alternate buffer object is deleted | restart returns to primary, restores primary cursor, discards inactive alternate UI state, and keeps the restored primary prompt area visible |
| Mode defaults after restart | explicit defaults in `modes.zig` | soft reset restores process-visible modes | reset/clear and ConPTY interactions keep terminal and console coherent | managed and Ghostty processors reset cursor, keyboard, mouse, paste, origin, wrap, margins, charsets, and style state |

## Findings Fixed

### 1. Controller-backed history operations were applied twice

`TerminalControl.ClearScrollback()` and `TerminalControl.ClearHistory()` called `ITerminalSessionHistoryController` and then also mutated the mirrored `TerminalScreen` directly. That made the control partially responsible for a mutation that the processor had already performed.

Why this was wrong:

- It blurred ownership between native-backed processors and the UI control.
- It could use cursor coordinates after the processor had already changed them.
- It did extra work on the UI thread.
- It diverged from Windows Terminal and Ghostty, where the terminal/backing engine owns the buffer mutation and the control synchronizes to that result.

Fix:

- `TerminalControl` now delegates to `ITerminalSessionHistoryController` exactly once.
- The direct `TerminalScreen` fallback is retained only for processors that do not expose the session-history controller capability.
- Added a control test proving that controller-backed commands do not trigger fallback screen mutation.

### 2. Ghostty-backed clear-history rebuilt the prompt row as plain text

`GhosttyVtProcessor.ClearVisibleHistory()` previously used `CSI 3J CSI 2J CSI H`, captured the cursor line as plain text, wrote that text back, and restored the cursor column. This preserved basic text but could lose foreground/background color, attributes, underline state, hyperlinks, grapheme fidelity, and any other native cell metadata.

Why this was wrong:

- xterm.js moves the existing buffer line object.
- Windows Terminal preserves the existing cursor row using erase/delete line operations.
- RoyalTerminal's managed path copies the existing `TerminalRow`, including formatting.
- Replaying plain text was therefore weaker than all comparable paths.

Fix:

- The Ghostty-backed clear-history sequence now keeps the native cursor row and edits around it:
  - erase scrollback,
  - erase rows below the cursor row,
  - delete rows above the cursor row,
  - restore the cursor column on row one.
- Removed the per-cell plain-text reconstruction helpers.
- Extended the Ghostty integration test to assert that the retained prompt row keeps bold/color attributes.

### 3. Prompt redraw was not process-aware in the demo command

The low-level clear-history API correctly mutates emulator state only. That is important for library callers because an emulator-side history operation should not unexpectedly inject input into a running process. The demo command exposed that low-level operation directly, which could leave interactive shells and prompt integrations with stale internal cursor state. On the next keypress, the shell could repaint at the old row and make recently cleared output appear to return.

Why this was wrong:

- Ghostty's host clear separates terminal-buffer mutation from process notification and sends form feed when prompt redraw is appropriate.
- xterm.js public `clear()` is also emulator-only, so hosts that need process-aware prompt repaint have to compose it explicitly.
- Windows Terminal/ConPTY clear paths keep the backing console state coherent; RoyalTerminal's cross-platform demo has to request shell repaint when the process owns prompt state.

Fix:

- Added `TerminalControl.RequestPromptRedraw()` to send form feed (`Ctrl+L`) through the normal input path.
- The demo `Clear History` command now calls `ClearHistory()`, scrolls to the live bottom, then calls `RequestPromptRedraw()`.
- Added a control test proving that prompt redraw sends the expected form-feed byte to the active transport.

### 4. Interrupted alternate-screen restart hid the restored primary prompt

The preserved restart path returned from alternate screen and then applied the same scroll-complete clear used for primary-screen restarts. That discarded the `mc` or `btop` UI correctly, but it also moved the just-restored primary shell prompt out of the live viewport. The result differed from reference terminals: xterm.js and Ghostty restore the primary cursor on `1049l`, and Windows Terminal reactivates the main buffer instead of treating the alternate buffer as durable history.

Why this was wrong:

- Alternate-screen exit is already the operation that exposes the durable primary buffer.
- Applying `CSI 22J` or `MoveViewportToScrollbackAndClear` after alternate-screen exit makes the restored prompt disappear from the live viewport.
- A restarted process cannot resume the old full-screen application, but it should inherit terminal state that looks like the application exited back to the shell.

Fix:

- `BasicVtProcessor` now detects preserved restarts that begin in alternate screen, exits alternate screen through the saved-primary-cursor path, discards inactive alternate rows, restores the primary cursor, and skips `MoveViewportToScrollbackAndClear`.
- `GhosttyVtProcessor` now exits native alternate screen first, refreshes the restored primary cursor from Ghostty, resets process-visible modes without `CSI 22J`, and restores that captured cursor after resetting margins.
- `TerminalControl` applies the same distinction in the fallback path for processors that do not implement `ITerminalSessionHistoryController`.
- Managed, native, and fallback tests now assert that interrupted `mc`/`btop`-style alternate-screen sessions return to the shell prompt and do not retain alternate-screen UI rows.

## Performance Impact

The reviewed operations are user-triggered lifecycle and history commands, not per-frame or per-byte VT parser hot paths. No hot-path regressions were found.

Performance effects of the fixes:

- Removing duplicate control-side mutations reduces UI-thread work for both managed and native processors.
- The Ghostty clear-history path no longer scans cells or builds a copied text row. It now builds a small fixed VT sequence based only on cursor coordinates.
- The demo prompt redraw adds one form-feed write after a user-triggered clear-history command. It does not run in parser, renderer, or output-drain hot paths.
- The alternate-screen restart path adds one native state check and one refresh around an explicit restart. It avoids the heavier scroll-complete clear for interrupted alternate-screen sessions.
- The preserved restart path still performs mode resets only on explicit restart and uses small fixed mode arrays. That cost is bounded and outside rendering/parser loops.
- Existing scrollback operations still use row-buffer moves/copies in `TerminalScreen`, which is appropriate for user-initiated history changes.

## Restart Semantics and TUI Applications

Reference terminals do not restore a terminated process. They preserve or clear terminal buffers and reset terminal modes; they do not reconstruct an application process such as a file manager or system monitor after the process has exited.

RoyalTerminal therefore provides terminal-state parity, not process-state parity:

- If a restart happens while a TUI application was using the alternate screen, the alternate-screen UI cannot be resumed because the process is gone.
- The primary buffer and preserved scrollback can be kept visible and usable, with the primary cursor restored when the alternate-screen entry mode saved it.
- A new shell or command can be started with terminal modes reset to sane defaults.
- To keep TUI applications alive across disconnects or terminal restarts, a process-level session manager such as a multiplexer is required outside the terminal buffer implementation.

This matches the boundary visible in Ghostty, xterm.js, and Windows Terminal: terminal buffers and VT modes are emulator state; running application state belongs to the process or PTY host.

## Validation Added or Updated

Relevant tests:

- `TerminalSessionHistoryTests`
- `GhosttyVtProcessorTests`
- `TerminalControlTests.Control_HistoryCommands_DelegateToSessionHistoryControllerWithoutFallbackMutation`
- `TerminalControlTests.Control_ClearScrollback_PreservesViewportAndDropsHistory`
- `TerminalControlTests.Control_ClearHistory_MakesPromptLineFirstViewportRow`
- `TerminalControlTests.Control_RequestPromptRedraw_SendsFormFeedToActiveTransport`

The focused test slice validates:

- clear scrollback preserves the viewport and drops history,
- clear history moves the cursor row to the first viewport row,
- managed preserved restart stores the previous viewport as scrollback,
- managed and fallback alternate-screen restarts restore the primary prompt instead of clearing it,
- Ghostty preserved restart retains styled native scrollback,
- Ghostty alternate-screen restart restores the primary prompt instead of retaining full-screen app UI,
- Ghostty clear-history preserves the styled prompt row,
- prompt redraw sends form feed to the active transport,
- `TerminalControl` does not apply fallback screen mutation after delegating to a session-history controller.
