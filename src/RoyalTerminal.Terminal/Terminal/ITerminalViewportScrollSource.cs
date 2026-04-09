// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Optional native viewport scrollback contract.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Snapshot of a terminal viewport's scrollback geometry.
/// </summary>
public readonly record struct TerminalViewportScrollState(
    ulong TotalRows,
    ulong OffsetRows,
    ulong VisibleRows)
{
    /// <summary>Gets the maximum top-anchored viewport offset.</summary>
    public ulong MaxOffsetRows => TotalRows > VisibleRows ? TotalRows - VisibleRows : 0;
}

/// <summary>
/// Optional VT processor capability for native viewport scrollback control.
/// </summary>
public interface ITerminalViewportScrollSource
{
    /// <summary>Gets the latest native viewport scrollback snapshot.</summary>
    TerminalViewportScrollState ViewportScrollState { get; }

    /// <summary>Scrolls the native viewport by a row delta.</summary>
    void ScrollViewportByRows(int deltaRows);

    /// <summary>Scrolls the native viewport to the top of scrollback.</summary>
    void ScrollViewportToTop();

    /// <summary>Scrolls the native viewport to the live bottom.</summary>
    void ScrollViewportToBottom();

    /// <summary>Moves the native viewport to an absolute top-anchored offset.</summary>
    void SetViewportOffsetRows(ulong offsetRows);
}
