// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for the official <c>GhosttyRenderState</c> libghostty-vt API.
/// </summary>
public sealed class GhosttyRenderState : IDisposable
{
    private nint _handle;
    private nint _rowIterator;
    private nint _rowCells;
    private bool _disposed;
    private readonly bool _ownsHandle;

    /// <summary>
    /// Creates a new render state with reusable row iterator and row cells handles.
    /// </summary>
    public GhosttyRenderState()
    {
        NativeLibraryLoader.Initialize();

        ThrowIfFailed(GhosttyVtNative.RenderStateNew(nint.Zero, out _handle), "ghostty_render_state_new");
        ThrowIfFailed(
            GhosttyVtNative.RenderStateRowIteratorNew(nint.Zero, out _rowIterator),
            "ghostty_render_state_row_iterator_new");
        ThrowIfFailed(
            GhosttyVtNative.RenderStateRowCellsNew(nint.Zero, out _rowCells),
            "ghostty_render_state_row_cells_new");

        _ownsHandle = true;
    }

    internal GhosttyRenderState(nint handle, bool ownsHandle = false)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
    }

    /// <summary>Returns true when the underlying render-state handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Refreshes this render-state snapshot from the given terminal.</summary>
    public void Update(GhosttyTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);

        ThrowIfFailed(GhosttyVtNative.RenderStateUpdate(_handle, terminal.Handle), "ghostty_render_state_update");
    }

    /// <summary>
    /// Begins a two-phase update while the caller still owns terminal synchronization.
    /// </summary>
    /// <remarks>
    /// Every successful call must be paired with <see cref="EndUpdate"/> before
    /// reading this render state.
    /// </remarks>
    public void BeginUpdate(GhosttyTerminal terminal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);

        ThrowIfFailed(
            GhosttyVtNative.RenderStateBeginUpdate(_handle, terminal.Handle),
            "ghostty_render_state_begin_update");
    }

    /// <summary>
    /// Completes a prior two-phase update without accessing terminal-owned memory.
    /// </summary>
    public void EndUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.RenderStateEndUpdate(_handle), "ghostty_render_state_end_update");
    }

    /// <summary>Gets the render-state dirty flag.</summary>
    public GhosttyVtNative.GhosttyRenderStateDirty GetDirty()
        => GetValue<GhosttyVtNative.GhosttyRenderStateDirty>(GhosttyVtNative.GhosttyRenderStateData.Dirty);

    /// <summary>Sets the render-state dirty flag.</summary>
    public unsafe void SetDirty(GhosttyVtNative.GhosttyRenderStateDirty value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyRenderStateDirty copy = value;
        ThrowIfFailed(
            GhosttyVtNative.RenderStateSet(_handle, GhosttyVtNative.GhosttyRenderStateOption.Dirty, &copy),
            "ghostty_render_state_set(dirty)");
    }

    /// <summary>Gets the render-state width in cells.</summary>
    public ushort GetColumns() => GetValue<ushort>(GhosttyVtNative.GhosttyRenderStateData.Cols);

    /// <summary>Gets the render-state height in cells.</summary>
    public ushort GetRows() => GetValue<ushort>(GhosttyVtNative.GhosttyRenderStateData.Rows);

    /// <summary>Gets the current render-state colors snapshot.</summary>
    public GhosttyVtNative.GhosttyRenderStateColors GetColors()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyRenderStateColors colors = GhosttyVtNative.GhosttyRenderStateColors.CreateSized();
        ThrowIfFailed(GhosttyVtNative.RenderStateColorsGet(_handle, ref colors), "ghostty_render_state_colors_get");
        return colors;
    }

    /// <summary>Gets the cursor visual style.</summary>
    public GhosttyVtNative.GhosttyRenderStateCursorVisualStyle GetCursorVisualStyle()
        => GetValue<GhosttyVtNative.GhosttyRenderStateCursorVisualStyle>(
            GhosttyVtNative.GhosttyRenderStateData.CursorVisualStyle);

    /// <summary>Gets whether the cursor is visible.</summary>
    public bool GetCursorVisible()
        => GetValue<bool>(GhosttyVtNative.GhosttyRenderStateData.CursorVisible);

    /// <summary>Gets whether the cursor is blinking.</summary>
    public bool GetCursorBlinking()
        => GetValue<bool>(GhosttyVtNative.GhosttyRenderStateData.CursorBlinking);

    /// <summary>Gets the current cursor viewport position when visible.</summary>
    public bool TryGetCursorViewport(out ushort x, out ushort y, out bool wideTail)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool hasValue = GetValue<bool>(
            GhosttyVtNative.GhosttyRenderStateData.CursorViewportHasValue);
        if (!hasValue)
        {
            x = 0;
            y = 0;
            wideTail = false;
            return false;
        }

        x = GetValue<ushort>(GhosttyVtNative.GhosttyRenderStateData.CursorViewportX);
        y = GetValue<ushort>(GhosttyVtNative.GhosttyRenderStateData.CursorViewportY);
        wideTail = GetValue<bool>(
            GhosttyVtNative.GhosttyRenderStateData.CursorViewportWideTail);
        return true;
    }

    /// <summary>Populates the reusable row iterator from the current render-state snapshot.</summary>
    public unsafe void BeginRows()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint iterator = _rowIterator;
        ThrowIfFailed(
            GhosttyVtNative.RenderStateGet(_handle, GhosttyVtNative.GhosttyRenderStateData.RowIterator, &iterator),
            "ghostty_render_state_get(row_iterator)");
        _rowIterator = iterator;
    }

    /// <summary>Moves to the next row in the reusable row iterator.</summary>
    public bool MoveNextRow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.RenderStateRowIteratorNext(_rowIterator);
    }

    /// <summary>Gets whether the current row is dirty.</summary>
    public bool GetCurrentRowDirty()
        => GetRowValue<bool>(GhosttyVtNative.GhosttyRenderStateRowData.Dirty);

    /// <summary>Gets the raw current row value.</summary>
    public ulong GetCurrentRowRaw()
        => GetRowValue<ulong>(GhosttyVtNative.GhosttyRenderStateRowData.Raw);

    /// <summary>Gets whether the current row soft-wraps into the next row.</summary>
    public bool GetCurrentRowWrap()
    {
        ulong row = GetCurrentRowRaw();
        return GetNativeRowFlag(row, GhosttyVtNative.GhosttyRowData.Wrap);
    }

    /// <summary>Gets the inclusive selected cell range intersecting the current row.</summary>
    public unsafe bool TryGetCurrentRowSelection(out ushort startX, out ushort endX)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyRenderStateRowSelection selection =
            GhosttyVtNative.GhosttyRenderStateRowSelection.CreateSized();
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.RenderStateRowGet(
            _rowIterator,
            GhosttyVtNative.GhosttyRenderStateRowData.Selection,
            &selection);

        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            startX = 0;
            endX = 0;
            return false;
        }

        ThrowIfFailed(result, "ghostty_render_state_row_get(selection)");
        startX = selection.StartX;
        endX = selection.EndX;
        return true;
    }

    /// <summary>Sets the current row dirty flag.</summary>
    public unsafe void SetCurrentRowDirty(bool value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool copy = value;
        ThrowIfFailed(
            GhosttyVtNative.RenderStateRowSet(_rowIterator, GhosttyVtNative.GhosttyRenderStateRowOption.Dirty, &copy),
            "ghostty_render_state_row_set(dirty)");
    }

    /// <summary>Populates the reusable row-cells iterator for the current row.</summary>
    public unsafe void BeginCurrentRowCells()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint cells = _rowCells;
        ThrowIfFailed(
            GhosttyVtNative.RenderStateRowGet(_rowIterator, GhosttyVtNative.GhosttyRenderStateRowData.Cells, &cells),
            "ghostty_render_state_row_get(cells)");
        _rowCells = cells;
    }

    /// <summary>Moves to the next cell in the current row.</summary>
    public bool MoveNextCell()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.RenderStateRowCellsNext(_rowCells);
    }

    /// <summary>Selects a specific cell in the current row.</summary>
    public void SelectCell(ushort x)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.RenderStateRowCellsSelect(_rowCells, x), "ghostty_render_state_row_cells_select");
    }

    /// <summary>Gets the raw current cell value.</summary>
    public ulong GetCurrentCellRaw()
        => GetCellValue<ulong>(GhosttyVtNative.GhosttyRenderStateRowCellsData.Raw);

    /// <summary>Gets the current cell style.</summary>
    public unsafe GhosttyVtNative.GhosttyStyle GetCurrentCellStyle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyStyle style = GhosttyVtNative.GhosttyStyle.CreateSized();
        ThrowIfFailed(
            GhosttyVtNative.RenderStateRowCellsGet(_rowCells, GhosttyVtNative.GhosttyRenderStateRowCellsData.Style, &style),
            "ghostty_render_state_row_cells_get(style)");
        return style;
    }

    /// <summary>Gets the number of codepoints in the current cell grapheme.</summary>
    public uint GetCurrentCellGraphemeLength()
        => GetCellValue<uint>(GhosttyVtNative.GhosttyRenderStateRowCellsData.GraphemesLength);

    /// <summary>Gets whether the current cell intersects the active selection.</summary>
    public bool GetCurrentCellSelected()
        => GetCellValue<bool>(GhosttyVtNative.GhosttyRenderStateRowCellsData.Selected);

    /// <summary>Gets whether the current cell has explicit style information.</summary>
    public bool GetCurrentCellHasStyling()
        => GetCellValue<bool>(GhosttyVtNative.GhosttyRenderStateRowCellsData.HasStyling);

    /// <summary>Copies the current cell grapheme codepoints into the provided buffer.</summary>
    public unsafe void GetCurrentCellGraphemes(Span<uint> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (destination.IsEmpty)
        {
            return;
        }

        fixed (uint* destinationPtr = destination)
        {
            ThrowIfFailed(
                GhosttyVtNative.RenderStateRowCellsGet(
                    _rowCells,
                    GhosttyVtNative.GhosttyRenderStateRowCellsData.GraphemesBuffer,
                    destinationPtr),
                "ghostty_render_state_row_cells_get(graphemes)");
        }
    }

    /// <summary>Copies the current cell's complete grapheme cluster as UTF-8.</summary>
    public unsafe byte[] CopyCurrentCellGraphemeUtf8()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        GhosttyVtNative.GhosttyBuffer buffer = default;
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.RenderStateRowCellsGet(
            _rowCells,
            GhosttyVtNative.GhosttyRenderStateRowCellsData.GraphemesUtf8,
            &buffer);
        if (probe == GhosttyVtNative.GhosttyResult.Success && buffer.Length == 0)
        {
            return [];
        }

        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_render_state_row_cells_get(graphemes_utf8 probe)");
        }

        byte[] result = new byte[checked((int)buffer.Length)];
        fixed (byte* resultPtr = result)
        {
            buffer.Pointer = resultPtr;
            buffer.Capacity = (nuint)result.Length;
            ThrowIfFailed(
                GhosttyVtNative.RenderStateRowCellsGet(
                    _rowCells,
                    GhosttyVtNative.GhosttyRenderStateRowCellsData.GraphemesUtf8,
                    &buffer),
                "ghostty_render_state_row_cells_get(graphemes_utf8)");
        }

        return result;
    }

    /// <summary>Attempts to get the current cell background color.</summary>
    public unsafe bool TryGetCurrentCellBackgroundColor(out GhosttyVtNative.GhosttyColorRgb color)
    {
        return TryGetCellColor(GhosttyVtNative.GhosttyRenderStateRowCellsData.BackgroundColor, out color);
    }

    /// <summary>Attempts to get the current cell foreground color.</summary>
    public unsafe bool TryGetCurrentCellForegroundColor(out GhosttyVtNative.GhosttyColorRgb color)
    {
        return TryGetCellColor(GhosttyVtNative.GhosttyRenderStateRowCellsData.ForegroundColor, out color);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_rowCells != nint.Zero)
        {
            GhosttyVtNative.RenderStateRowCellsFree(_rowCells);
            _rowCells = nint.Zero;
        }

        if (_rowIterator != nint.Zero)
        {
            GhosttyVtNative.RenderStateRowIteratorFree(_rowIterator);
            _rowIterator = nint.Zero;
        }

        if (_ownsHandle && _handle != nint.Zero)
        {
            GhosttyVtNative.RenderStateFree(_handle);
        }

        _handle = nint.Zero;
    }

    private unsafe bool TryGetCellColor(
        GhosttyVtNative.GhosttyRenderStateRowCellsData data,
        out GhosttyVtNative.GhosttyColorRgb color)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyColorRgb value = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.RenderStateRowCellsGet(_rowCells, data, &value);
        if (result == GhosttyVtNative.GhosttyResult.InvalidValue || result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            color = default;
            return false;
        }

        ThrowIfFailed(result, $"ghostty_render_state_row_cells_get({data})");
        color = value;
        return true;
    }

    private unsafe T GetRowValue<T>(GhosttyVtNative.GhosttyRenderStateRowData data) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T value = default;
        ThrowIfFailed(GhosttyVtNative.RenderStateRowGet(_rowIterator, data, &value), $"ghostty_render_state_row_get({data})");
        return value;
    }

    private unsafe bool GetNativeRowFlag(ulong row, GhosttyVtNative.GhosttyRowData data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool value = false;
        ThrowIfFailed(GhosttyVtNative.RowGet(row, data, &value), $"ghostty_row_get({data})");
        return value;
    }

    private unsafe T GetCellValue<T>(GhosttyVtNative.GhosttyRenderStateRowCellsData data) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T value = default;
        ThrowIfFailed(GhosttyVtNative.RenderStateRowCellsGet(_rowCells, data, &value), $"ghostty_render_state_row_cells_get({data})");
        return value;
    }

    private unsafe T GetValue<T>(GhosttyVtNative.GhosttyRenderStateData data)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T value = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.RenderStateGet(_handle, data, &value);
        ThrowIfFailed(result, $"ghostty_render_state_get({data})");
        return value;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }
}
