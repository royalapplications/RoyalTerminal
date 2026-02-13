// Licensed under the MIT License.
// GhosttySharp.Avalonia - Terminal cell model.

using SkiaSharp;

namespace GhosttySharp.Avalonia.Rendering;

/// <summary>
/// Represents a single cell in the terminal grid.
/// Designed for efficient storage in contiguous arrays.
/// </summary>
public struct TerminalCell
{
    /// <summary>UTF-32 codepoint for this cell. 0 for empty cells.</summary>
    public int Codepoint;

    /// <summary>Foreground color as packed ARGB.</summary>
    public uint Foreground;

    /// <summary>Background color as packed ARGB.</summary>
    public uint Background;

    /// <summary>Cell attribute flags.</summary>
    public CellAttributes Attributes;

    /// <summary>Number of columns this character spans (1 for normal, 2 for wide/CJK).</summary>
    public byte Width;

    /// <summary>Returns true if this cell has content.</summary>
    public readonly bool HasContent => Codepoint != 0;

    /// <summary>Returns the foreground as an SKColor.</summary>
    public readonly SKColor ForegroundColor => new(Foreground);

    /// <summary>Returns the background as an SKColor.</summary>
    public readonly SKColor BackgroundColor => new(Background);

    /// <summary>Creates a default empty cell with the given colors.</summary>
    public static TerminalCell Empty(uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E) => new()
    {
        Codepoint = 0,
        Foreground = fg,
        Background = bg,
        Attributes = CellAttributes.None,
        Width = 1,
    };
}

/// <summary>
/// Cell attribute flags packed into a single byte.
/// </summary>
[Flags]
public enum CellAttributes : byte
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Strikethrough = 1 << 3,
    Inverse = 1 << 4,
    Blink = 1 << 5,
    Dim = 1 << 6,
    Hidden = 1 << 7,
}

/// <summary>
/// Represents a row of terminal cells with dirty tracking.
/// </summary>
public sealed class TerminalRow
{
    private readonly TerminalCell[] _cells;

    /// <summary>Whether this row has been modified since last render.</summary>
    public bool IsDirty { get; set; } = true;

    /// <summary>Number of columns in this row.</summary>
    public int Columns => _cells.Length;

    /// <summary>Access the cells array as a span.</summary>
    public Span<TerminalCell> Cells => _cells.AsSpan();

    /// <summary>Read-only access to cells.</summary>
    public ReadOnlySpan<TerminalCell> ReadOnlyCells => _cells.AsSpan();

    public TerminalRow(int columns, uint defaultFg = 0xFFD4D4D4, uint defaultBg = 0xFF1E1E1E)
    {
        _cells = new TerminalCell[columns];
        Clear(defaultFg, defaultBg);
    }

    /// <summary>Access a cell by column index.</summary>
    public ref TerminalCell this[int column] => ref _cells[column];

    /// <summary>Clear all cells to the default state.</summary>
    public void Clear(uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E)
    {
        for (var i = 0; i < _cells.Length; i++)
            _cells[i] = TerminalCell.Empty(fg, bg);
        IsDirty = true;
    }
}

/// <summary>
/// Screen buffer holding a grid of terminal cells with optional scrollback.
/// Supports virtualized access for large scroll buffers.
/// </summary>
public sealed class TerminalScreen
{
    private readonly List<TerminalRow> _rows;
    private int _scrollbackLimit;
    private int _viewportTop;

    /// <summary>Number of visible rows in the viewport.</summary>
    public int ViewportRows { get; private set; }

    /// <summary>Number of columns per row.</summary>
    public int Columns { get; private set; }

    /// <summary>Total rows including scrollback.</summary>
    public int TotalRows => _rows.Count;

    /// <summary>Current scroll position (0 = bottom/latest).</summary>
    public int ScrollOffset
    {
        get => _viewportTop;
        set => _viewportTop = Math.Clamp(value, 0, MaxScrollOffset);
    }

    /// <summary>Maximum scroll offset.</summary>
    public int MaxScrollOffset => Math.Max(0, TotalRows - ViewportRows);

    /// <summary>Default foreground color.</summary>
    public uint DefaultForeground { get; set; } = 0xFFD4D4D4;

    /// <summary>Default background color.</summary>
    public uint DefaultBackground { get; set; } = 0xFF1E1E1E;

    /// <summary>Lock object for thread-safe access from UI and composition threads.</summary>
    public object SyncRoot { get; } = new object();

    public TerminalScreen(int columns, int viewportRows, int scrollbackLimit = 10_000)
    {
        Columns = columns;
        ViewportRows = viewportRows;
        _scrollbackLimit = scrollbackLimit;
        _rows = new List<TerminalRow>(viewportRows);

        // Initialize visible rows
        for (var i = 0; i < viewportRows; i++)
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
    }

    /// <summary>
    /// Gets a row relative to the viewport.
    /// </summary>
    public TerminalRow GetViewportRow(int viewportRow)
    {
        var absoluteRow = TotalRows - ViewportRows + _viewportTop + viewportRow;
        absoluteRow = Math.Clamp(absoluteRow, 0, TotalRows - 1);
        return _rows[absoluteRow];
    }

    /// <summary>
    /// Gets a row by absolute index.
    /// </summary>
    public TerminalRow GetRow(int absoluteRow) => _rows[absoluteRow];

    /// <summary>
    /// Adds a new row to the bottom, potentially scrolling up.
    /// </summary>
    public TerminalRow AddRow()
    {
        var row = new TerminalRow(Columns, DefaultForeground, DefaultBackground);
        _rows.Add(row);

        // Trim scrollback if exceeding limit
        while (_rows.Count > ViewportRows + _scrollbackLimit)
        {
            _rows.RemoveAt(0);
        }

        return row;
    }

    /// <summary>
    /// Resizes the screen to new dimensions.
    /// </summary>
    public void Resize(int columns, int viewportRows)
    {
        var oldColumns = Columns;
        Columns = columns;
        ViewportRows = viewportRows;

        // Resize existing rows if column count changed
        if (columns != oldColumns)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Columns != columns)
                {
                    var newRow = new TerminalRow(columns, DefaultForeground, DefaultBackground);
                    var copyCount = Math.Min(_rows[i].Columns, columns);
                    var oldCells = _rows[i].ReadOnlyCells;
                    var newCells = newRow.Cells;
                    oldCells[..copyCount].CopyTo(newCells);
                    _rows[i] = newRow;
                }
            }
        }

        // Ensure we have enough rows
        while (_rows.Count < viewportRows)
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));

        // Mark all dirty for re-render
        foreach (var row in _rows)
            row.IsDirty = true;
    }

    /// <summary>
    /// Marks all rows as dirty for a full repaint.
    /// </summary>
    public void InvalidateAll()
    {
        foreach (var row in _rows)
            row.IsDirty = true;
    }

    /// <summary>
    /// Checks if any visible row is dirty.
    /// </summary>
    public bool HasDirtyRows()
    {
        for (var i = 0; i < ViewportRows && i < TotalRows; i++)
        {
            if (GetViewportRow(i).IsDirty)
                return true;
        }
        return false;
    }
}
