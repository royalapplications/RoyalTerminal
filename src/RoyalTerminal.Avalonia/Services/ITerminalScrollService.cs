// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Terminal scrolling coordination abstraction.

using Avalonia.Input;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Coordinates scroll state updates and scroll input handling.
/// </summary>
public interface ITerminalScrollService
{
    /// <summary>
    /// Applies scroll updates after terminal output processing.
    /// </summary>
    void HandleOutput(
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        bool autoScroll,
        TerminalPresenter? presenter,
        Action raiseScrollInvalidated);

    /// <summary>
    /// Scrolls the viewport by row count.
    /// </summary>
    void ScrollByRows(
        int rows,
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        TerminalPresenter? presenter);

    /// <summary>
    /// Scrolls viewport to the bottom.
    /// </summary>
    void ScrollToBottom(
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        TerminalPresenter? presenter);

    /// <summary>
    /// Handles mouse wheel input for terminal scrolling.
    /// </summary>
    void HandlePointerWheel(
        PointerWheelEventArgs e,
        VirtualizedTerminalScrollViewer? scrollViewer,
        ITerminalSessionService sessionService,
        TerminalPresenter? presenter,
        Action raiseScrollInvalidated);
}
