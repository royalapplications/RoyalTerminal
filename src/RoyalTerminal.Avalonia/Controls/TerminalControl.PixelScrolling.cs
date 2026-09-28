// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Defines the opt-in pixel-scrolling property.</summary>
    public static readonly DirectProperty<TerminalControl, bool> PixelScrollingEnabledProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(PixelScrollingEnabled),
            control => control.PixelScrollingEnabled, (control, value) => control.PixelScrollingEnabled = value);

    private bool _pixelScrollingEnabled;
    private Point? _pixelPointerPoint;

    /// <summary>
    /// Preserves fractional scrollbar/wheel movement and aligns text, images and
    /// input geometry with the presented rows. Default false retains legacy row
    /// scrolling. Native processors need the fractional-viewport capability.
    /// This does not add a time-based scroll animation.
    /// </summary>
    public bool PixelScrollingEnabled
    {
        get => _pixelScrollingEnabled;
        set
        {
            if (!SetAndRaise(PixelScrollingEnabledProperty, ref _pixelScrollingEnabled, value)) return;
            if (!value && _screen is not null && _vtProcessor is ITerminalFractionalViewportScrollSource fractional)
                lock (_screen.SyncRoot) fractional.SetViewportScrollPosition(new(fractional.PublishedViewportPosition.TopRow, 0));
            SyncScreenScrollOffsetFromScrollData();
            UpdateRendererCursorForViewport();
            UpdateRendererParityStateFromScreen();
            RefreshPixelPointer();
            _textInputMethodClient?.NotifyCursorChanged();
            _presenter?.Invalidate(fullRedraw: true);
        }
    }

    private bool UsesPixelScrolling => PixelScrollingEnabled &&
        (_vtProcessor is not ITerminalViewportScrollSource || _vtProcessor is ITerminalFractionalViewportScrollSource);

    private int PresentedRowCount => _screen is null ? 0 : _screen.ViewportRows + _screen.RenderScrollOverscan.Below;

    private TerminalRow GetPresentedRowLocked(int row)
        => _screen!.GetRenderViewport(_screen.RenderScrollOverscan)[row].Row;

    private void RefreshPixelPointer()
    {
        if (!UsesPixelScrolling || _pixelPointerPoint is not Point point || _lastPointerRow < 0) return;
        _lastPointerRow = -1;
        UpdatePointerCell(point);
    }

    private TerminalViewportScrollPosition GetPixelScrollPosition(ulong maximumRow)
        => _scrollData is null ? default : TerminalViewportScrollPosition.FromPixels(_scrollData.Offset, _scrollData.MaxOffset, maximumRow);

    private void SyncManagedRenderScrollFractionLocked()
    {
        if (_screen is null || _scrollData is null || _vtProcessor is ITerminalViewportScrollSource) return;
        _screen.RenderScrollFraction = UsesPixelScrolling
            ? GetPixelScrollPosition((ulong)_screen.MaxScrollOffset).FractionalRow : 0;
    }

    private bool TryHandlePixelWheel(double delta)
    {
        if (!UsesPixelScrolling || _scrollData is null || !_scrollData.CanScroll || _screen is null || !double.IsFinite(delta)) return false;
        CaptureRendererSelectionForCurrentViewport();
        _scrollData.ScrollByPixels(-delta * 3 * _scrollData.CellHeight);
        SyncScreenScrollOffsetFromScrollData();
        UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen(invalidateViewportRows: true);
        UpdateAutoScrollPinnedToBottom();
        UpdatePreservedRestartHistoryInputScrollGuardForViewportChange();
        _textInputMethodClient?.NotifyCursorChanged();
        _presenter?.Invalidate();
        RaiseScrollInvalidated();
        return true;
    }
}
