// Licensed under the MIT License.
// RoyalTerminal.Avalonia — Terminal scrolling coordination abstraction.

using Avalonia.Input;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.GhosttySharp.Terminal.Services;

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
        bool autoScroll,
        GhosttyTerminalPresenter? presenter,
        Action raiseScrollInvalidated);

    /// <summary>
    /// Scrolls the viewport by row count.
    /// </summary>
    void ScrollByRows(
        int rows,
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        GhosttyTerminalPresenter? presenter);

    /// <summary>
    /// Scrolls viewport to the bottom.
    /// </summary>
    void ScrollToBottom(
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        GhosttyTerminalPresenter? presenter);

    /// <summary>
    /// Handles mouse wheel input for terminal scrolling.
    /// </summary>
    void HandlePointerWheel(
        PointerWheelEventArgs e,
        VirtualizedTerminalScrollViewer? scrollViewer,
        ITerminalSessionService sessionService,
        ITerminalInputAdapter inputAdapter,
        GhosttyTerminalPresenter? presenter,
        Action raiseScrollInvalidated);
}
