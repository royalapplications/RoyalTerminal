// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal scroll data for virtualized scrolling.

using Avalonia;

namespace RoyalTerminal.Avalonia.Scrolling;

/// <summary>
/// Tracks scroll state for the terminal, supporting large scroll buffers
/// with efficient viewport-relative access.
/// </summary>
public class TerminalScrollData
{
    private double _offset;
    private double _extent;
    private double _viewport;
    private double _cellHeight;

    /// <summary>Current scroll offset in pixels.</summary>
    public double Offset
    {
        get => _offset;
        set => _offset = Math.Clamp(value, 0, MaxOffset);
    }

    /// <summary>Total scrollable extent in pixels.</summary>
    public double Extent
    {
        get => _extent;
        set => _extent = Math.Max(0, value);
    }

    /// <summary>Viewport height in pixels.</summary>
    public double Viewport
    {
        get => _viewport;
        set => _viewport = Math.Max(0, value);
    }

    /// <summary>Maximum scroll offset in pixels.</summary>
    public double MaxOffset => Math.Max(0, Extent - Viewport);

    /// <summary>Height of a single cell/row in pixels.</summary>
    public double CellHeight
    {
        get => _cellHeight;
        set => _cellHeight = Math.Max(1, value);
    }

    /// <summary>Current scroll offset in rows.</summary>
    public int OffsetRows => _cellHeight > 0 ? (int)(Offset / _cellHeight) : 0;

    /// <summary>Number of visible rows in the viewport.</summary>
    public int ViewportRows => _cellHeight > 0 ? (int)Math.Ceiling(Viewport / _cellHeight) : 0;

    /// <summary>Total number of rows in the extent.</summary>
    public int TotalRows => _cellHeight > 0 ? (int)Math.Ceiling(Extent / _cellHeight) : 0;

    /// <summary>Whether the viewport is scrolled to the bottom.</summary>
    public bool IsAtBottom => Offset >= MaxOffset - 1;

    /// <summary>Whether scrolling is possible.</summary>
    public bool CanScroll => Extent > Viewport;

    /// <summary>
    /// Updates the scroll state for a new total row count.
    /// Optionally auto-scrolls to bottom if we were already there.
    /// </summary>
    public void UpdateExtent(int totalRows, bool autoScrollToBottom)
    {
        var wasAtBottom = IsAtBottom;
        Extent = totalRows * CellHeight;

        if (autoScrollToBottom && wasAtBottom)
            Offset = MaxOffset;
    }

    /// <summary>
    /// Scrolls by the given number of rows (positive = down, negative = up).
    /// </summary>
    public void ScrollByRows(int rows)
    {
        Offset += rows * CellHeight;
    }

    /// <summary>
    /// Scrolls by the given number of pixels.
    /// </summary>
    public void ScrollByPixels(double delta)
    {
        Offset += delta;
    }

    /// <summary>
    /// Scrolls to the bottom of the buffer.
    /// </summary>
    public void ScrollToBottom()
    {
        Offset = MaxOffset;
    }

    /// <summary>
    /// Scrolls to the top of the buffer.
    /// </summary>
    public void ScrollToTop()
    {
        Offset = 0;
    }

    /// <summary>
    /// Scrolls by one page up.
    /// </summary>
    public void PageUp()
    {
        Offset -= Viewport;
    }

    /// <summary>
    /// Scrolls by one page down.
    /// </summary>
    public void PageDown()
    {
        Offset += Viewport;
    }

    /// <summary>
    /// Converts a viewport-relative Y position to an absolute row index.
    /// </summary>
    public int ViewportYToRow(double y)
    {
        return OffsetRows + (int)(y / CellHeight);
    }

    /// <summary>
    /// Gets the scrollbar thumb size as a proportion of the viewport.
    /// </summary>
    public double ThumbSize => Extent > 0 ? Math.Min(1, Viewport / Extent) : 1;

    /// <summary>
    /// Gets the scrollbar thumb position as a proportion.
    /// </summary>
    public double ThumbPosition => MaxOffset > 0 ? Offset / MaxOffset : 0;
}
