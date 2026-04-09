// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal cell model.

using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Avalonia.Rendering;

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
    private readonly TerminalCell[] _cells;

    /// <summary>Whether this row has been modified since last render.</summary>
    public bool IsDirty { get; set; } = true;

    /// <summary>
    /// Whether this row soft-wraps into the following row.
    /// Explicit line feeds keep this false.
    /// </summary>
    public bool WrapsToNext { get; set; }

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
        WrapsToNext = false;
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
    private readonly Dictionary<int, string> _hyperlinksById = [];
    private readonly Dictionary<string, int> _hyperlinkIdsByUrl = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TerminalKittyImageSource> _kittyImagesById = [];
    private TerminalKittyImagePlacement[] _kittyPlacements = Array.Empty<TerminalKittyImagePlacement>();
    private int _nextHyperlinkId = 1;
    private int _scrollbackLimit;
    private int _viewportTop;
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
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            TerminalRow row = _rows[rowIndex];
            Span<TerminalCell> cells = row.Cells;
            bool changed = false;

            for (int col = 0; col < cells.Length; col++)
            {
                ref TerminalCell cell = ref cells[col];

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
                row.IsDirty = true;
            }
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
                    newRow.WrapsToNext = _rows[i].WrapsToNext;
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
