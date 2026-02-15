// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default terminal scroll service.

using Avalonia.Input;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.GhosttySharp.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

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
            RoyalTerminal.GhosttySharp.Native.GhosttyMods mods = inputAdapter.ConvertModifiers(e.KeyModifiers);
            sessionService.Surface.SendMouseScroll(e.Delta.X, e.Delta.Y, (int)mods);
        }

        presenter?.Invalidate();
        raiseScrollInvalidated();
    }
}
