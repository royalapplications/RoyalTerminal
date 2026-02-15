// Licensed under the MIT License.
// GhosttySharp.Avalonia — Terminal scrolling coordination abstraction.

using Avalonia.Input;
using GhosttySharp.Avalonia.Controls;
using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Avalonia.Scrolling;
using GhosttySharp.Terminal.Services;

namespace GhosttySharp.Avalonia.Services;

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
