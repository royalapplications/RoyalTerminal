// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default terminal scroll service.

using Avalonia.Input;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Default implementation of terminal scroll coordination.
/// </summary>
public sealed class DefaultTerminalScrollService : ITerminalScrollService
{
    /// <inheritdoc />
    public void HandleOutput(
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        bool autoScroll,
        TerminalPresenter? presenter,
        Action raiseScrollInvalidated)
    {
        if (scrollData is not null && screen is not null)
        {
            scrollData.UpdateExtent(screen.TotalRows, autoScroll);
        }

        SyncScreenScrollOffset(scrollData, screen);

        presenter?.Invalidate();
        raiseScrollInvalidated();
    }

    /// <inheritdoc />
    public void ScrollByRows(
        int rows,
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        TerminalPresenter? presenter)
    {
        scrollData?.ScrollByRows(rows);
        SyncScreenScrollOffset(scrollData, screen);
        presenter?.Invalidate();
    }

    /// <inheritdoc />
    public void ScrollToBottom(
        TerminalScrollData? scrollData,
        TerminalScreen? screen,
        TerminalPresenter? presenter)
    {
        scrollData?.ScrollToBottom();
        SyncScreenScrollOffset(scrollData, screen);
        presenter?.Invalidate();
    }

    /// <inheritdoc />
    public void HandlePointerWheel(
        PointerWheelEventArgs e,
        VirtualizedTerminalScrollViewer? scrollViewer,
        ITerminalSessionService sessionService,
        TerminalPresenter? presenter,
        Action raiseScrollInvalidated)
    {
        scrollViewer?.HandleWheel(e.Delta.Y);

        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is not null)
        {
            TerminalPointerEvent pointerEvent = new(
                Kind: TerminalPointerEventKind.Scroll,
                X: 0,
                Y: 0,
                Button: TerminalMouseButton.None,
                Action: TerminalInputAction.Press,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
                DeltaX: e.Delta.X,
                DeltaY: e.Delta.Y);
            inputSink.SendPointer(pointerEvent);
        }

        presenter?.Invalidate();
        raiseScrollInvalidated();
    }

    private static TerminalModifiers ConvertTerminalModifiers(KeyModifiers keyModifiers)
    {
        TerminalModifiers mods = TerminalModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= TerminalModifiers.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= TerminalModifiers.Control;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= TerminalModifiers.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= TerminalModifiers.Meta;
        return mods;
    }

    private static void SyncScreenScrollOffset(TerminalScrollData? scrollData, TerminalScreen? screen)
    {
        if (scrollData is null || screen is null)
        {
            return;
        }

        lock (screen.SyncRoot)
        {
            int nextOffset = scrollData.ToScreenScrollOffsetRows(screen.MaxScrollOffset);
            if (screen.ScrollOffset == nextOffset)
            {
                return;
            }

            screen.ScrollOffset = nextOffset;
            screen.InvalidateViewport();
        }
    }
}
