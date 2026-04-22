// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal cell model.

using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Zero-based terminal grid position in viewport coordinates.
/// </summary>
public readonly record struct TerminalGridPosition(int Column, int Row);

/// <summary>
/// Represents a single cell in the terminal grid.
/// Designed for efficient storage in contiguous arrays.
/// </summary>
public struct TerminalCell
{
    /// <summary>UTF-32 codepoint for this cell. 0 for empty cells.</summary>
    public int Codepoint;

    /// <summary>
    /// Optional grapheme text for this cell.
    /// When null, rendering/copy should use <see cref="Codepoint"/>.
    /// </summary>
    public string? Grapheme;

    /// <summary>Foreground color as packed ARGB.</summary>
    public uint Foreground;

    /// <summary>Background color as packed ARGB.</summary>
    public uint Background;

    /// <summary>Cell attribute flags.</summary>
    public CellAttributes Attributes;

    /// <summary>Underline rendering style for this cell.</summary>
    public TerminalUnderlineStyle UnderlineStyle;

    /// <summary>Optional explicit underline color as packed ARGB.</summary>
    public uint UnderlineColor;

    /// <summary>Whether <see cref="UnderlineColor"/> should be used.</summary>
    public bool HasUnderlineColor;

    /// <summary>Extended decoration flags not represented in <see cref="CellAttributes"/>.</summary>
    public CellDecorations Decorations;

    /// <summary>
    /// Whether this cell has an explicit background set (vs inherited/default background).
    /// </summary>
    public bool HasBackground;

    /// <summary>
    /// Hyperlink token id for OSC8 links. Zero means no hyperlink.
    /// </summary>
    public int HyperlinkId;

    /// <summary>Number of columns this character spans (1 for normal, 2 for wide/CJK).</summary>
    public byte Width;

    /// <summary>Returns true if this cell has content.</summary>
    public readonly bool HasContent => Codepoint != 0 || !string.IsNullOrEmpty(Grapheme);

    /// <summary>Creates a default empty cell with the given colors.</summary>
    public static TerminalCell Empty(uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E) => new()
    {
        Codepoint = 0,
        Grapheme = null,
        Foreground = fg,
        Background = bg,
        Attributes = CellAttributes.None,
        UnderlineStyle = TerminalUnderlineStyle.None,
        UnderlineColor = 0,
        HasUnderlineColor = false,
        Decorations = CellDecorations.None,
        HasBackground = true,
        HyperlinkId = 0,
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
/// Underline style for terminal cell decorations.
/// </summary>
public enum TerminalUnderlineStyle : byte
{
    None = 0,
    Single = 1,
    Double = 2,
    Curly = 3,
    Dotted = 4,
    Dashed = 5,
}

/// <summary>
/// Additional cell decorations not captured by <see cref="CellAttributes"/>.
/// </summary>
[Flags]
public enum CellDecorations : byte
{
    None = 0,
    Overline = 1 << 0,
}

/// <summary>
/// Highlight category for managed renderer overlay spans.
/// </summary>
public enum TerminalHighlightKind : byte
{
    SearchMatch = 0,
    SearchSelected = 1,
    HyperlinkHover = 2,
}

/// <summary>
/// Row-local highlight span in viewport coordinates.
/// </summary>
public readonly record struct TerminalHighlightSpan(
    int Row,
    int StartColumn,
    int EndColumn,
    TerminalHighlightKind Kind)
{
    /// <summary>Returns true when this span contains the specified cell.</summary>
    public bool Contains(int row, int column)
    {
        return row == Row && column >= StartColumn && column <= EndColumn;
    }
}

/// <summary>
/// Represents a row of terminal cells with dirty tracking.
/// </summary>
public sealed class TerminalRow
{
    private TerminalCell[] _cells;
    private int _columns;

    /// <summary>Whether this row has been modified since last render.</summary>
    public bool IsDirty { get; set; } = true;

    /// <summary>
    /// Whether this row soft-wraps into the following row.
    /// Explicit line feeds keep this false.
    /// </summary>
    public bool WrapsToNext { get; set; }

    /// <summary>Number of columns in this row.</summary>
    public int Columns => _columns;

    /// <summary>Number of cells retained in backing storage, including cells hidden by a narrower resize.</summary>
    public int PreservedColumns => _cells.Length;

    /// <summary>Access the cells array as a span.</summary>
    public Span<TerminalCell> Cells => _cells.AsSpan(0, _columns);

    /// <summary>Read-only access to cells.</summary>
    public ReadOnlySpan<TerminalCell> ReadOnlyCells => _cells.AsSpan(0, _columns);

    /// <summary>Read-only access to all retained cells, including cells hidden by a narrower resize.</summary>
    public ReadOnlySpan<TerminalCell> ReadOnlyPreservedCells => _cells.AsSpan();

    public TerminalRow(int columns, uint defaultFg = 0xFFD4D4D4, uint defaultBg = 0xFF1E1E1E)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columns);

        _columns = columns;
        _cells = new TerminalCell[columns];
        Clear(defaultFg, defaultBg);
    }

    /// <summary>Access a cell by column index.</summary>
    public ref TerminalCell this[int column] => ref _cells[column];

    /// <summary>Resize the active row width without discarding preserved cells.</summary>
    public void Resize(int columns, uint defaultFg = 0xFFD4D4D4, uint defaultBg = 0xFF1E1E1E)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columns);

        EnsureCapacity(columns, defaultFg, defaultBg);
        _columns = columns;
        IsDirty = true;
    }

    /// <summary>Copies active and retained cells from another row while keeping this row's active width.</summary>
    public void CopyFrom(TerminalRow source, uint defaultFg = 0xFFD4D4D4, uint defaultBg = 0xFF1E1E1E)
    {
        ArgumentNullException.ThrowIfNull(source);

        int preservedColumns = Math.Max(_columns, source.PreservedColumns);
        ResizePreservedStorage(preservedColumns, defaultFg, defaultBg);

        ReadOnlySpan<TerminalCell> sourceCells = source.ReadOnlyPreservedCells;
        sourceCells.CopyTo(_cells);
        for (int i = sourceCells.Length; i < _cells.Length; i++)
        {
            _cells[i] = TerminalCell.Empty(defaultFg, defaultBg);
        }

        WrapsToNext = source.WrapsToNext;
        IsDirty = true;
    }

    /// <summary>Clears cells retained outside the active width after this row is edited while narrow.</summary>
    public void ClearPreservedCellsFrom(int column, uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E)
    {
        int start = Math.Clamp(column, 0, _cells.Length);
        if (start >= _cells.Length)
        {
            return;
        }

        if (start < _columns)
        {
            for (int i = start; i < _columns; i++)
            {
                _cells[i] = TerminalCell.Empty(fg, bg);
            }

            ResizePreservedStorage(_columns, fg, bg);
            IsDirty = true;
            return;
        }

        if (start == _columns)
        {
            ResizePreservedStorage(_columns, fg, bg);
            IsDirty = true;
            return;
        }

        for (int i = start; i < _cells.Length; i++)
        {
            _cells[i] = TerminalCell.Empty(fg, bg);
        }

        IsDirty = true;
    }

    /// <summary>Remaps cell colors across active and retained cells.</summary>
    internal bool RemapCellColors(IReadOnlyDictionary<uint, uint> colorRemap)
    {
        bool changed = false;
        for (int col = 0; col < _cells.Length; col++)
        {
            ref TerminalCell cell = ref _cells[col];

            if (colorRemap.TryGetValue(cell.Foreground, out uint mappedFg))
            {
                cell.Foreground = mappedFg;
                changed = true;
            }

            if (colorRemap.TryGetValue(cell.Background, out uint mappedBg))
            {
                cell.Background = mappedBg;
                changed = true;
            }
        }

        if (changed)
        {
            IsDirty = true;
        }

        return changed;
    }

    /// <summary>Clear all cells to the default state.</summary>
    public void Clear(uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E)
    {
        ResizePreservedStorage(_columns, fg, bg);
        for (var i = 0; i < _columns; i++)
            _cells[i] = TerminalCell.Empty(fg, bg);
        WrapsToNext = false;
        IsDirty = true;
    }

    private void EnsureCapacity(int columns, uint defaultFg, uint defaultBg)
    {
        if (columns <= _cells.Length)
        {
            return;
        }

        ResizePreservedStorage(columns, defaultFg, defaultBg);
    }

    private void ResizePreservedStorage(int columns, uint defaultFg, uint defaultBg)
    {
        if (columns == _cells.Length)
        {
            return;
        }

        int previousLength = _cells.Length;
        Array.Resize(ref _cells, columns);
        for (int i = previousLength; i < _cells.Length; i++)
        {
            _cells[i] = TerminalCell.Empty(defaultFg, defaultBg);
        }
    }
}

/// <summary>
/// Screen buffer holding a grid of terminal cells with optional scrollback.
/// Supports virtualized access for large scroll buffers.
/// </summary>
public sealed class TerminalScreen
{
    private List<TerminalRow> _rows;
    private List<TerminalRow>? _primaryRows;
    private List<TerminalRow>? _alternateRows;
    private readonly Dictionary<int, string> _hyperlinksById = [];
    private readonly Dictionary<string, int> _hyperlinkIdsByUrl = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TerminalKittyImageSource> _kittyImagesById = [];
    private TerminalKittyImagePlacement[] _kittyPlacements = Array.Empty<TerminalKittyImagePlacement>();
    private int _nextHyperlinkId = 1;
    private int _scrollbackLimit;
    private int _viewportTop;
    private int _primaryScrollOffset;
    private bool _alternateBufferActive;
    private TerminalTheme _theme = TerminalTheme.Dark;
    private long _themeRevision;

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

    /// <summary>Whether the screen is currently rendering the alternate buffer.</summary>
    public bool AlternateBufferActive => _alternateBufferActive;

    /// <summary>Default foreground color.</summary>
    public uint DefaultForeground { get; set; } = 0xFFD4D4D4;

    /// <summary>Default background color.</summary>
    public uint DefaultBackground { get; set; } = 0xFF1E1E1E;

    /// <summary>The active immutable terminal theme snapshot.</summary>
    public TerminalTheme Theme => _theme;

    /// <summary>
    /// Monotonically increasing theme revision.
    /// Incremented each time <see cref="ApplyTheme"/> is called.
    /// </summary>
    public long ThemeRevision => _themeRevision;

    /// <summary>Lock object for thread-safe access from UI and composition threads.</summary>
    public object SyncRoot { get; } = new object();

    /// <summary>Gets whether the current viewport snapshot includes Kitty image placements.</summary>
    public bool HasKittyGraphics => _kittyPlacements.Length > 0;

    public TerminalScreen(int columns, int viewportRows, int scrollbackLimit = 10_000)
    {
        Columns = columns;
        ViewportRows = viewportRows;
        _scrollbackLimit = scrollbackLimit;
        _rows = new List<TerminalRow>(viewportRows);
        _theme = _theme
            .WithDefaultForeground(DefaultForeground)
            .WithDefaultBackground(DefaultBackground)
            .WithCursorColor(DefaultForeground);

        // Initialize visible rows
        for (var i = 0; i < viewportRows; i++)
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
    }

    /// <summary>
    /// Applies a new immutable theme snapshot to this screen.
    /// </summary>
    public void ApplyTheme(TerminalTheme theme, bool invalidateRows = true)
    {
        ArgumentNullException.ThrowIfNull(theme);

        TerminalTheme previousTheme = _theme;
        Dictionary<uint, uint> colorRemap = BuildColorRemap(previousTheme, theme);

        if (colorRemap.Count > 0)
        {
            RemapExistingCellColors(colorRemap);
        }

        _theme = theme;
        _themeRevision++;
        DefaultForeground = theme.DefaultForeground;
        DefaultBackground = theme.DefaultBackground;

        if (invalidateRows)
        {
            InvalidateAll();
        }
    }

    private void RemapExistingCellColors(IReadOnlyDictionary<uint, uint> colorRemap)
    {
        RemapRowColors(_rows, colorRemap);

        if (_primaryRows is not null && !ReferenceEquals(_primaryRows, _rows))
        {
            RemapRowColors(_primaryRows, colorRemap);
        }

        if (_alternateRows is not null && !ReferenceEquals(_alternateRows, _rows))
        {
            RemapRowColors(_alternateRows, colorRemap);
        }
    }

    private static void RemapRowColors(List<TerminalRow> rows, IReadOnlyDictionary<uint, uint> colorRemap)
    {
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rows[rowIndex].RemapCellColors(colorRemap);
        }
    }

    private static Dictionary<uint, uint> BuildColorRemap(TerminalTheme previousTheme, TerminalTheme nextTheme)
    {
        Dictionary<uint, uint> remap = new(capacity: 258);
        HashSet<uint> ambiguousSources = new();

        AddColorRemap(remap, ambiguousSources, previousTheme.DefaultForeground, nextTheme.DefaultForeground);
        AddColorRemap(remap, ambiguousSources, previousTheme.DefaultBackground, nextTheme.DefaultBackground);

        for (int i = 0; i < 256; i++)
        {
            AddColorRemap(remap, ambiguousSources, previousTheme.Palette[i], nextTheme.Palette[i]);
        }

        return remap;
    }

    private static void AddColorRemap(
        IDictionary<uint, uint> remap,
        ISet<uint> ambiguousSources,
        uint source,
        uint target)
    {
        if (source == target || ambiguousSources.Contains(source))
        {
            return;
        }

        if (!remap.TryGetValue(source, out uint existing))
        {
            remap[source] = target;
            return;
        }

        if (existing != target)
        {
            remap.Remove(source);
            ambiguousSources.Add(source);
        }
    }

    /// <summary>
    /// Resolves an indexed ANSI/256 palette color from the active theme.
    /// </summary>
    public uint ResolvePaletteColor(int index)
    {
        if ((uint)index >= 256)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _theme.Palette[index];
    }

    /// <summary>
    /// Gets a row relative to the viewport.
    /// </summary>
    public TerminalRow GetViewportRow(int viewportRow)
    {
        var absoluteRow = TotalRows - ViewportRows - _viewportTop + viewportRow;
        absoluteRow = Math.Clamp(absoluteRow, 0, TotalRows - 1);
        return _rows[absoluteRow];
    }

    /// <summary>
    /// Gets a row by absolute index.
    /// </summary>
    public TerminalRow GetRow(int absoluteRow) => _rows[absoluteRow];

    /// <summary>
    /// Registers a hyperlink URL and returns its stable token id.
    /// </summary>
    public int RegisterHyperlink(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (_hyperlinkIdsByUrl.TryGetValue(url, out int existingId))
        {
            return existingId;
        }

        int nextId = _nextHyperlinkId;
        if (nextId == int.MaxValue)
        {
            // Reuse existing registry to avoid integer overflow while keeping currently
            // active ids stable for already-rendered cells.
            nextId = 1;
            while (_hyperlinksById.ContainsKey(nextId))
            {
                nextId++;
            }
        }

        _nextHyperlinkId = nextId + 1;
        _hyperlinksById[nextId] = url;
        _hyperlinkIdsByUrl[url] = nextId;
        return nextId;
    }

    /// <summary>
    /// Resolves an OSC8 hyperlink token id to its URL.
    /// </summary>
    public bool TryGetHyperlinkUrl(int hyperlinkId, out string? url)
    {
        if (hyperlinkId <= 0)
        {
            url = null;
            return false;
        }

        return _hyperlinksById.TryGetValue(hyperlinkId, out url);
    }

    /// <summary>Gets the current Kitty image placement snapshot.</summary>
    public ReadOnlySpan<TerminalKittyImagePlacement> GetKittyPlacements() => _kittyPlacements;

    /// <summary>Attempts to resolve a Kitty image payload by image id.</summary>
    public bool TryGetKittyImageSource(int imageId, out TerminalKittyImageSource? source)
    {
        if (imageId <= 0)
        {
            source = null;
            return false;
        }

        return _kittyImagesById.TryGetValue(imageId, out source);
    }

    /// <summary>Replaces the current Kitty image snapshot.</summary>
    public void ReplaceKittyGraphics(
        IReadOnlyList<TerminalKittyImageSource>? images,
        IReadOnlyList<TerminalKittyImagePlacement>? placements)
    {
        _kittyImagesById.Clear();
        if (images is not null)
        {
            for (int i = 0; i < images.Count; i++)
            {
                TerminalKittyImageSource image = images[i];
                _kittyImagesById[image.ImageId] = image;
            }
        }

        if (placements is null || placements.Count == 0)
        {
            _kittyPlacements = Array.Empty<TerminalKittyImagePlacement>();
        }
        else
        {
            TerminalKittyImagePlacement[] copy = new TerminalKittyImagePlacement[placements.Count];
            for (int i = 0; i < placements.Count; i++)
            {
                copy[i] = placements[i];
            }

            _kittyPlacements = copy;
        }

        InvalidateViewport();
    }

    /// <summary>Clears the current Kitty image snapshot.</summary>
    public void ClearKittyGraphics()
    {
        if (_kittyImagesById.Count == 0 && _kittyPlacements.Length == 0)
        {
            return;
        }

        _kittyImagesById.Clear();
        _kittyPlacements = Array.Empty<TerminalKittyImagePlacement>();
        InvalidateViewport();
    }

    /// <summary>
    /// Adds a new row to the bottom, potentially scrolling up.
    /// </summary>
    public TerminalRow AddRow()
    {
        var row = new TerminalRow(Columns, DefaultForeground, DefaultBackground);
        _rows.Add(row);

        // Trim scrollback if exceeding limit
        int maxRows = ViewportRows + (_alternateBufferActive ? 0 : _scrollbackLimit);
        while (_rows.Count > maxRows)
        {
            _rows.RemoveAt(0);
        }

        return row;
    }

    /// <summary>
    /// Switches the active backing rows to an isolated alternate screen buffer.
    /// </summary>
    public void SwitchToAlternateBuffer(bool clear)
    {
        if (_alternateBufferActive)
        {
            ScrollOffset = 0;
            EnsureAlternateRows();
            if (clear)
            {
                ClearActiveRows();
            }

            InvalidateAll();
            return;
        }

        _primaryRows = _rows;
        _primaryScrollOffset = _viewportTop;
        _alternateBufferActive = true;
        _viewportTop = 0;

        _alternateRows ??= new List<TerminalRow>(ViewportRows);
        _rows = _alternateRows;
        EnsureAlternateRows();

        if (clear)
        {
            ClearActiveRows();
        }

        InvalidateAll();
    }

    /// <summary>
    /// Restores the primary screen buffer after alternate screen use.
    /// </summary>
    public void SwitchToPrimaryBuffer()
    {
        if (!_alternateBufferActive)
        {
            return;
        }

        _alternateRows = _rows;
        _rows = _primaryRows ?? CreateRows(Columns, ViewportRows, DefaultForeground, DefaultBackground);
        _primaryRows = null;
        _alternateBufferActive = false;

        ResizeActiveRows(Columns);
        EnsureMinimumRows(ViewportRows);
        TrimScrollbackRows();
        ScrollOffset = _primaryScrollOffset;
        InvalidateAll();
    }

    /// <summary>
    /// Releases any inactive alternate screen rows.
    /// </summary>
    public void DiscardInactiveAlternateBuffer()
    {
        if (_alternateBufferActive)
        {
            return;
        }

        _alternateRows = null;
    }

    /// <summary>
    /// Appends blank rows until the bottom-anchored viewport starts at or after the requested absolute row.
    /// </summary>
    public void PadBottomViewportToPreserveTop(int minimumViewportTopAbsoluteRow)
    {
        if (_alternateBufferActive)
        {
            EnsureAlternateRows();
            ScrollOffset = 0;
            return;
        }

        int targetTop = Math.Clamp(minimumViewportTopAbsoluteRow, 0, Math.Max(0, _scrollbackLimit));
        int missingRows = targetTop - Math.Max(0, TotalRows - ViewportRows);
        for (int i = 0; i < missingRows; i++)
        {
            AddRow();
        }

        ScrollOffset = 0;
    }

    private void EnsureAlternateRows()
    {
        ResizeActiveRows(Columns);
        EnsureMinimumRows(ViewportRows);
        if (_rows.Count > ViewportRows)
        {
            _rows.RemoveRange(ViewportRows, _rows.Count - ViewportRows);
        }

        ScrollOffset = 0;
    }

    private void ClearActiveRows()
    {
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            _rows[rowIndex].Clear(DefaultForeground, DefaultBackground);
        }
    }

    private void ResizeActiveRows(int columns)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Resize(columns, DefaultForeground, DefaultBackground);
        }
    }

    private void EnsureMinimumRows(int rows)
    {
        while (_rows.Count < rows)
        {
            _rows.Add(new TerminalRow(Columns, DefaultForeground, DefaultBackground));
        }
    }

    private void TrimScrollbackRows()
    {
        while (_rows.Count > ViewportRows + _scrollbackLimit)
        {
            _rows.RemoveAt(0);
        }
    }

    private static List<TerminalRow> CreateRows(int columns, int rows, uint defaultForeground, uint defaultBackground)
    {
        List<TerminalRow> result = new(rows);
        for (int i = 0; i < rows; i++)
        {
            result.Add(new TerminalRow(columns, defaultForeground, defaultBackground));
        }

        return result;
    }

    /// <summary>
    /// Resizes the screen to new dimensions.
    /// </summary>
    public TerminalGridPosition Resize(
        int columns,
        int viewportRows,
        bool reflowOnResize = true,
        TerminalGridPosition? trackedViewportPosition = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewportRows, 1);

        int oldColumns = Columns;
        int trackedAbsoluteRow = trackedViewportPosition is { } trackedPosition
            ? GetAbsoluteRowForViewportRow(trackedPosition.Row)
            : -1;
        int trackedColumn = trackedViewportPosition is { } position
            ? Math.Clamp(position.Column, 0, oldColumns)
            : 0;
        int mappedAbsoluteRow = trackedAbsoluteRow;
        int mappedColumn = Math.Clamp(trackedColumn, 0, columns);

        if (columns != oldColumns)
        {
            if (reflowOnResize)
            {
                ReflowRows(columns, trackedAbsoluteRow, trackedColumn, out mappedAbsoluteRow, out mappedColumn);
            }
            else
            {
                ResizeRows(columns);
            }
        }

        Columns = columns;
        ViewportRows = viewportRows;

        while (_rows.Count < viewportRows)
        {
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
        }

        int removedRows = 0;
        if (_alternateBufferActive)
        {
            if (_rows.Count > ViewportRows)
            {
                _rows.RemoveRange(ViewportRows, _rows.Count - ViewportRows);
            }
        }
        else
        {
            while (_rows.Count > ViewportRows + _scrollbackLimit)
            {
                _rows.RemoveAt(0);
                removedRows++;
            }
        }

        if (mappedAbsoluteRow >= 0)
        {
            mappedAbsoluteRow -= removedRows;
        }

        ScrollOffset = _viewportTop;

        foreach (TerminalRow row in _rows)
        {
            row.IsDirty = true;
        }

        return GetViewportPositionForAbsoluteRow(mappedAbsoluteRow, mappedColumn);
    }

    private void ResizeRows(int columns)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Resize(columns, DefaultForeground, DefaultBackground);
        }
    }

    private int GetAbsoluteRowForViewportRow(int viewportRow)
    {
        if (_rows.Count == 0)
        {
            return -1;
        }

        int clampedViewportRow = Math.Clamp(viewportRow, 0, Math.Max(0, ViewportRows - 1));
        int absoluteRow = TotalRows - ViewportRows - _viewportTop + clampedViewportRow;
        return Math.Clamp(absoluteRow, 0, TotalRows - 1);
    }

    private TerminalGridPosition GetViewportPositionForAbsoluteRow(int absoluteRow, int column)
    {
        int clampedColumn = Math.Clamp(column, 0, Columns);
        if (_rows.Count == 0)
        {
            return new TerminalGridPosition(clampedColumn, 0);
        }

        int viewportStart = Math.Max(0, TotalRows - ViewportRows - _viewportTop);
        int viewportRow = Math.Clamp(absoluteRow - viewportStart, 0, Math.Max(0, ViewportRows - 1));
        return new TerminalGridPosition(clampedColumn, viewportRow);
    }

    private void ReflowRows(
        int columns,
        int trackedAbsoluteRow,
        int trackedColumn,
        out int mappedAbsoluteRow,
        out int mappedColumn)
    {
        List<TerminalRow> reflowedRows = new(_rows.Count);
        List<TerminalCell> logicalLine = new(Math.Max(Columns, columns));
        mappedAbsoluteRow = trackedAbsoluteRow;
        mappedColumn = Math.Clamp(trackedColumn, 0, columns);

        int rowIndex = 0;
        while (rowIndex < _rows.Count)
        {
            logicalLine.Clear();
            int trackedLogicalOffset = -1;

            bool hasContinuation;
            do
            {
                TerminalRow row = _rows[rowIndex];
                int endExclusive = GetReflowEndExclusive(row);
                if (rowIndex == trackedAbsoluteRow)
                {
                    int clampedTrackedColumn = Math.Clamp(trackedColumn, 0, row.Columns);
                    endExclusive = Math.Max(endExclusive, clampedTrackedColumn);
                    trackedLogicalOffset = logicalLine.Count + clampedTrackedColumn;
                }

                if (endExclusive > 0)
                {
                    ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells[..endExclusive];
                    for (int i = 0; i < cells.Length; i++)
                    {
                        logicalLine.Add(cells[i]);
                    }
                }

                hasContinuation = row.WrapsToNext && rowIndex + 1 < _rows.Count;
                rowIndex++;
            }
            while (hasContinuation);

            int destinationStartRow = reflowedRows.Count;
            TerminalGridPosition? mappedLinePosition = AppendReflowedLogicalLine(
                logicalLine,
                columns,
                reflowedRows,
                trackedLogicalOffset);

            if (mappedLinePosition is { } mappedPosition)
            {
                mappedAbsoluteRow = destinationStartRow + mappedPosition.Row;
                mappedColumn = mappedPosition.Column;
            }
        }

        _rows.Clear();
        _rows.AddRange(reflowedRows);
    }

    private int GetReflowEndExclusive(TerminalRow row)
    {
        if (row.WrapsToNext)
        {
            return row.Columns;
        }

        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            if (IsReflowMeaningfulCell(in cells[i]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private bool IsReflowMeaningfulCell(ref readonly TerminalCell cell)
    {
        return cell.HasContent ||
            cell.Attributes != CellAttributes.None ||
            cell.UnderlineStyle != TerminalUnderlineStyle.None ||
            cell.HasUnderlineColor ||
            cell.Decorations != CellDecorations.None ||
            cell.HyperlinkId != 0 ||
            cell.Foreground != DefaultForeground ||
            cell.Background != DefaultBackground ||
            !cell.HasBackground;
    }

    private TerminalGridPosition? AppendReflowedLogicalLine(
        List<TerminalCell> logicalLine,
        int columns,
        List<TerminalRow> destination,
        int trackedLogicalOffset)
    {
        int destinationStart = destination.Count;
        TerminalGridPosition? mappedPosition = null;

        if (logicalLine.Count == 0)
        {
            destination.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
            return trackedLogicalOffset >= 0
                ? new TerminalGridPosition(0, 0)
                : null;
        }

        int sourceIndex = 0;
        while (sourceIndex < logicalLine.Count)
        {
            TerminalRow row = new(columns, DefaultForeground, DefaultBackground);
            int destinationRow = destination.Count - destinationStart;
            int column = 0;

            while (sourceIndex < logicalLine.Count && column < columns)
            {
                if (mappedPosition is null && trackedLogicalOffset >= 0 && trackedLogicalOffset <= sourceIndex)
                {
                    mappedPosition = new TerminalGridPosition(column, destinationRow);
                }

                TerminalCell cell = logicalLine[sourceIndex];
                if (cell.Width == 0)
                {
                    sourceIndex++;
                    continue;
                }

                int sourceStep = GetReflowSourceStep(logicalLine, sourceIndex);
                int width = cell.Width <= 1 ? 1 : 2;
                if (width == 2 && columns == 1)
                {
                    sourceIndex += sourceStep;
                    column++;

                    if (mappedPosition is null && trackedLogicalOffset >= 0 && trackedLogicalOffset <= sourceIndex)
                    {
                        mappedPosition = new TerminalGridPosition(column, destinationRow);
                    }

                    continue;
                }

                if (width == 2 && column == columns - 1)
                {
                    break;
                }

                cell.Width = (byte)width;
                row[column] = cell;

                if (width == 2 && column + 1 < columns)
                {
                    row[column + 1] = CreateWideSpacer(cell);
                }

                sourceIndex += sourceStep;
                column += width;

                if (mappedPosition is null && trackedLogicalOffset >= 0 && trackedLogicalOffset <= sourceIndex)
                {
                    mappedPosition = new TerminalGridPosition(column, destinationRow);
                }
            }

            row.WrapsToNext = sourceIndex < logicalLine.Count;
            destination.Add(row);
        }

        if (mappedPosition is null && trackedLogicalOffset >= 0)
        {
            int lastRow = Math.Max(0, destination.Count - destinationStart - 1);
            mappedPosition = new TerminalGridPosition(0, lastRow);
        }

        return mappedPosition;
    }

    private static int GetReflowSourceStep(List<TerminalCell> logicalLine, int sourceIndex)
    {
        TerminalCell cell = logicalLine[sourceIndex];
        if (cell.Width <= 1)
        {
            return 1;
        }

        return sourceIndex + 1 < logicalLine.Count && logicalLine[sourceIndex + 1].Width == 0
            ? 2
            : 1;
    }

    private static TerminalCell CreateWideSpacer(TerminalCell source) => new()
    {
        Codepoint = 0,
        Grapheme = null,
        Foreground = source.Foreground,
        Background = source.Background,
        Attributes = source.Attributes,
        UnderlineStyle = source.UnderlineStyle,
        UnderlineColor = source.UnderlineColor,
        HasUnderlineColor = source.HasUnderlineColor,
        Decorations = source.Decorations,
        HasBackground = source.HasBackground,
        HyperlinkId = source.HyperlinkId,
        Width = 0,
    };

    /// <summary>
    /// Marks all rows as dirty for a full repaint.
    /// </summary>
    public void InvalidateAll()
    {
        lock (SyncRoot)
        {
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].IsDirty = true;
        }
    }

    /// <summary>
    /// Marks only the currently visible viewport rows as dirty.
    /// </summary>
    public void InvalidateViewport()
    {
        lock (SyncRoot)
        {
            int viewportStart = Math.Max(0, TotalRows - ViewportRows - _viewportTop);
            int viewportEnd = Math.Min(TotalRows, viewportStart + ViewportRows);
            for (int rowIndex = viewportStart; rowIndex < viewportEnd; rowIndex++)
            {
                _rows[rowIndex].IsDirty = true;
            }
        }
    }

    /// <summary>
    /// Checks if any visible row is dirty.
    /// </summary>
    public bool HasDirtyRows()
    {
        lock (SyncRoot)
        {
            for (var i = 0; i < ViewportRows && i < TotalRows; i++)
            {
                if (GetViewportRow(i).IsDirty)
                    return true;
            }
        }

        return false;
    }
}
