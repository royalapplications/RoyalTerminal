// Licensed under the MIT License.
// GhosttySharp.Avalonia — Default terminal scroll service.

using Avalonia.Input;
using GhosttySharp.Avalonia.Controls;
using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Avalonia.Scrolling;
using GhosttySharp.Terminal.Services;

namespace GhosttySharp.Avalonia.Services;

/// <summary>
/// Default implementation of terminal scroll coordination.
/// </summary>
public sealed class DefaultTerminalScrollService : ITerminalScrollService
{
    /// <inheritdoc />
    public void HandleOutput(
        TerminalScrollData? scrollData,
        bool autoScroll,
        GhosttyTerminalPresenter? presenter,
        Action raiseScrollInvalidated)
    {
        if (autoScroll)
        {
            scrollData?.ScrollToBottom();
        }

        presenter?.Invalidate();
        raiseScrollInvalidated();
    }

    /// <inheritdoc />
    public void ScrollByRows(
        int rows,
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        GhosttyTerminalPresenter? presenter)
    {
        scrollData?.ScrollByRows(rows);
        screen?.InvalidateAll();
        presenter?.Invalidate();
    }

    /// <inheritdoc />
    public void ScrollToBottom(
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        GhosttyTerminalPresenter? presenter)
    {
        scrollData?.ScrollToBottom();
        screen?.InvalidateAll();
        presenter?.Invalidate();
    }

    /// <inheritdoc />
    public void HandlePointerWheel(
        PointerWheelEventArgs e,
        VirtualizedTerminalScrollViewer? scrollViewer,
        ITerminalSessionService sessionService,
        ITerminalInputAdapter inputAdapter,
        GhosttyTerminalPresenter? presenter,
        Action raiseScrollInvalidated)
    {
        scrollViewer?.HandleWheel(e.Delta.Y);

        if (sessionService.Surface is not null)
        {
            GhosttySharp.Native.GhosttyMods mods = inputAdapter.ConvertModifiers(e.KeyModifiers);
            sessionService.Surface.SendMouseScroll(e.Delta.X, e.Delta.Y, (int)mods);
        }

        presenter?.Invalidate();
        raiseScrollInvalidated();
    }
}
