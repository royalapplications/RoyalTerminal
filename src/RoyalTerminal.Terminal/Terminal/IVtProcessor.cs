// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — VT processor abstraction.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Abstraction over a VT sequence processor that translates raw terminal output
/// into screen state (cells, cursor position, modes). Two implementations exist:
/// <list type="bullet">
///   <item>
///     <see cref="BasicVtProcessor"/> — pure C# fallback processor, always available.
///   </item>
///   <item>
///     <see cref="GhosttyVtProcessor"/> — wraps Ghostty's native VT processor via
///     libghostty-vt for more complete / accurate VT processing.
///   </item>
/// </list>
/// </summary>
public interface IVtProcessor : IDisposable
{
    /// <summary>Current cursor column (0-based).</summary>
    int CursorCol { get; }

    /// <summary>Current cursor row (0-based).</summary>
    int CursorRow { get; }

    /// <summary>Whether the cursor should be visible (DECTCEM mode 25).</summary>
    bool CursorVisible { get; }

    /// <summary>Whether application cursor key mode is active (DECCKM mode 1).</summary>
    bool ApplicationCursorKeys { get; }

    /// <summary>Whether application keypad mode is active.</summary>
    bool ApplicationKeypad { get; }

    /// <summary>Whether the alternate screen buffer is active.</summary>
    bool AlternateScreen { get; }

    /// <summary>Whether bracketed paste mode is active.</summary>
    bool BracketedPaste { get; }

    /// <summary>Whether win32 input mode (DECSET 9001) is active.</summary>
    bool Win32InputMode { get; }

    /// <summary>Current snapshot of VT mode flags.</summary>
    TerminalModeState ModeState { get; }

    /// <summary>
    /// Raised when VT mode flags change.
    /// </summary>
    event EventHandler<TerminalModeState>? ModeChanged;

    /// <summary>
    /// Processes a span of raw terminal output bytes, updating internal state
    /// and the associated <see cref="RoyalTerminal.Avalonia.Rendering.TerminalScreen"/>.
    /// </summary>
    void Process(ReadOnlySpan<byte> data);

    /// <summary>
    /// Notifies the processor that the terminal has been resized.
    /// Overload without pixel dimensions.
    /// </summary>
    void NotifyResize(int columns, int rows);

    /// <summary>
    /// Notifies the processor that the terminal has been resized with pixel dimensions.
    /// Pixel dimensions are used for CSI 14t/16t size reports.
    /// </summary>
    void NotifyResize(int columns, int rows, int widthPx, int heightPx);

    /// <summary>
    /// Resets the processor to its initial state.
    /// </summary>
    void Reset();

    /// <summary>
    /// Callback invoked when the terminal needs to send a response back to the
    /// input source (e.g., DSR cursor position report, DA device attributes).
    /// The byte array contains the raw response bytes to write to the PTY.
    /// </summary>
    Action<byte[]>? ResponseCallback { get; set; }

    /// <summary>
    /// Callback invoked when the terminal bell is triggered.
    /// </summary>
    Action? BellCallback { get; set; }

    /// <summary>
    /// Callback invoked when the terminal title changes.
    /// The string parameter is the new title.
    /// </summary>
    Action<string>? TitleCallback { get; set; }
}
