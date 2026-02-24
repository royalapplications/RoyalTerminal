// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Virtualized terminal scroll viewer.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Avalonia.Scrolling;

/// <summary>
/// Implements <see cref="ILogicalScrollable"/> for efficient virtualized scrolling
/// of the terminal content. Integrates with Avalonia's ScrollViewer.
/// </summary>
public class VirtualizedTerminalScrollViewer : Control, ILogicalScrollable
{
    private readonly TerminalScrollData _scrollData;
    private readonly TerminalScreen _screen;
    private Size _scrollSize = new(1, 1);
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll = true;

    /// <summary>Event raised when the scroll state changes.</summary>
    public event EventHandler? ScrollInvalidated;

    /// <summary>The underlying scroll data.</summary>
    public TerminalScrollData ScrollData => _scrollData;

    /// <summary>The terminal screen being scrolled.</summary>
    public TerminalScreen Screen => _screen;

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => ScrollInvalidated += value;
        remove => ScrollInvalidated -= value;
    }

    public VirtualizedTerminalScrollViewer(TerminalScreen screen, TerminalScrollData scrollData)
    {
        _screen = screen;
        _scrollData = scrollData;
    }

    /// <summary>Whether this control supports logical scrolling.</summary>
    public bool IsLogicalScrollEnabled => true;

    /// <summary>The scroll size (how much to scroll per unit).</summary>
    public Size ScrollSize => _scrollSize;

    /// <summary>The page scroll size.</summary>
    public Size PageScrollSize => new(Bounds.Width, Bounds.Height);

    bool ILogicalScrollable.CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    bool ILogicalScrollable.CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    /// <summary>The total extent of the scrollable content.</summary>
    public Size Extent => new(Bounds.Width, _scrollData.Extent);

    /// <summary>The scroll offset position.</summary>
    public Vector Offset
    {
        get => new(0, _scrollData.Offset);
        set
        {
            _scrollData.Offset = value.Y;
            UpdateScreenScroll();
            InvalidateScrollInfo();
        }
    }

    /// <summary>The viewport size.</summary>
    public Size Viewport => new(Bounds.Width, _scrollData.Viewport);

    /// <summary>
    /// Brings a control into view (not applicable for terminal).
    /// </summary>
    public bool BringIntoView(Control target, Rect targetRect) => false;

    /// <summary>
    /// Returns the control to scroll into view (not applicable for terminal).
    /// </summary>
    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    /// <summary>
    /// Raises the <see cref="ScrollInvalidated"/> event to notify the scroll viewer.
    /// </summary>
    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);

    /// <summary>
    /// Updates internal state when viewport size changes.
    /// </summary>
    public void UpdateViewport(double viewportHeight, double cellHeight)
    {
        _scrollData.Viewport = viewportHeight;
        _scrollData.CellHeight = cellHeight;
        _scrollSize = new Size(10, cellHeight);
        InvalidateScrollInfo();
    }

    /// <summary>
    /// Notifies that the total content rows have changed.
    /// </summary>
    public void UpdateExtent(int totalRows, bool autoScrollToBottom = true)
    {
        _scrollData.UpdateExtent(totalRows, autoScrollToBottom);
        InvalidateScrollInfo();
    }

    /// <summary>
    /// Handles mouse wheel scrolling.
    /// </summary>
    public void HandleWheel(double delta)
    {
        _scrollData.ScrollByRows(-(int)(delta * 3));
        UpdateScreenScroll();
        InvalidateScrollInfo();
    }

    private void UpdateScreenScroll()
    {
        int nextOffset = _scrollData.ToScreenScrollOffsetRows(_screen.MaxScrollOffset);
        if (_screen.ScrollOffset == nextOffset)
        {
            return;
        }

        _screen.ScrollOffset = nextOffset;
        _screen.InvalidateAll();
    }

    private void InvalidateScrollInfo()
    {
        ScrollInvalidated?.Invoke(this, EventArgs.Empty);
    }
}
