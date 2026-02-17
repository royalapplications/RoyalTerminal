// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Snapshot of terminal mode flags.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Captures VT mode state used by terminal input and UI orchestration.
/// </summary>
/// <param name="CursorVisible">Whether the cursor is visible.</param>
/// <param name="ApplicationCursorKeys">Whether DECCKM mode is active.</param>
/// <param name="ApplicationKeypad">Whether application keypad mode is active.</param>
/// <param name="AlternateScreen">Whether alternate screen buffer is active.</param>
/// <param name="BracketedPaste">Whether bracketed paste mode is active.</param>
public readonly record struct TerminalModeState(
    bool CursorVisible,
    bool ApplicationCursorKeys,
    bool ApplicationKeypad,
    bool AlternateScreen,
    bool BracketedPaste);
