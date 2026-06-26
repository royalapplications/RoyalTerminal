// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal cell model.

using System.Runtime.InteropServices;
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
    Selection = 3,
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

    /// <summary>
    /// True when this row was appended only to preserve a Windows PTY live viewport during resize.
    /// </summary>
    internal bool IsTransientResizeRow { get; set; }

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
        IsTransientResizeRow = source.IsTransientResizeRow;
        IsDirty = true;
    }

    /// <summary>Copies only the active cells from another row while discarding retained hidden columns.</summary>
    public void CopyActiveFrom(TerminalRow source, uint defaultFg = 0xFFD4D4D4, uint defaultBg = 0xFF1E1E1E)
    {
        ArgumentNullException.ThrowIfNull(source);

        ResizePreservedStorage(_columns, defaultFg, defaultBg);

        ReadOnlySpan<TerminalCell> sourceCells = source.ReadOnlyCells;
        int copiedColumns = Math.Min(_columns, sourceCells.Length);
        sourceCells[..copiedColumns].CopyTo(_cells);
        for (int i = copiedColumns; i < _columns; i++)
        {
            _cells[i] = TerminalCell.Empty(defaultFg, defaultBg);
        }

        WrapsToNext = source.WrapsToNext;
        IsTransientResizeRow = source.IsTransientResizeRow;
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
        IsTransientResizeRow = false;
        IsDirty = true;
    }

    /// <summary>Marks this row as real terminal content after an external cell mutation.</summary>
    internal void MarkContentMutation()
    {
        IsTransientResizeRow = false;
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
/// Ring-backed terminal row buffer optimized for scrollback trimming at the top.
/// </summary>
internal sealed class TerminalRowBuffer
{
    private TerminalRow?[] _items;
    private int _head;

    public TerminalRowBuffer(int capacity = 0)
    {
        _items = capacity > 0
            ? new TerminalRow[capacity]
            : Array.Empty<TerminalRow>();
    }

    public int Count { get; private set; }

    public TerminalRow this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count);
            return _items[PhysicalIndex(index)]!;
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count);
            _items[PhysicalIndex(index)] = value;
        }
    }

    public void Add(TerminalRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        EnsureCapacity(Count + 1);
        _items[PhysicalIndex(Count)] = row;
        Count++;
    }

    public void AddRange(IEnumerable<TerminalRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows is ICollection<TerminalRow> collection)
        {
            EnsureCapacity(Count + collection.Count);
        }

        foreach (TerminalRow row in rows)
        {
            Add(row);
        }
    }

    public void Clear()
    {
        if (Count == 0)
        {
            return;
        }

        if (_head + Count <= _items.Length)
        {
            Array.Clear(_items, _head, Count);
        }
        else
        {
            int firstSegmentLength = _items.Length - _head;
            Array.Clear(_items, _head, firstSegmentLength);
            Array.Clear(_items, 0, Count - firstSegmentLength);
        }

        _head = 0;
        Count = 0;
    }

    public void RemoveFirst(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Count);
        if (count == 0)
        {
            return;
        }

        ClearPhysicalRange(_head, count);
        _head = Count == count ? 0 : (_head + count) % _items.Length;
        Count -= count;
    }

    public void RemoveRange(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (index > Count || count > Count - index)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        if (index == 0)
        {
            RemoveFirst(count);
            return;
        }

        if (index + count == Count)
        {
            RemoveTail(count);
            return;
        }

        Compact();
        Array.Copy(
            _items,
            index + count,
            _items,
            index,
            Count - index - count);
        Array.Clear(_items, Count - count, count);
        Count -= count;
    }

    public IEnumerator<TerminalRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    private void RemoveTail(int count)
    {
        ClearPhysicalRange(PhysicalIndex(Count - count), count);
        Count -= count;
        if (Count == 0)
        {
            _head = 0;
        }
    }

    private void EnsureCapacity(int desiredCapacity)
    {
        if (desiredCapacity <= _items.Length)
        {
            return;
        }

        int nextCapacity = _items.Length == 0 ? 4 : _items.Length * 2;
        while (nextCapacity < desiredCapacity)
        {
            nextCapacity *= 2;
        }

        TerminalRow?[] nextItems = new TerminalRow[nextCapacity];
        CopyLogicalTo(nextItems);
        _items = nextItems;
        _head = 0;
    }

    private void Compact()
    {
        if (_head == 0 || Count == 0)
        {
            return;
        }

        TerminalRow?[] compacted = new TerminalRow[Math.Max(Count, _items.Length)];
        CopyLogicalTo(compacted);
        _items = compacted;
        _head = 0;
    }

    private void CopyLogicalTo(TerminalRow?[] destination)
    {
        if (Count == 0)
        {
            return;
        }

        if (_head + Count <= _items.Length)
        {
            Array.Copy(_items, _head, destination, 0, Count);
            return;
        }

        int firstSegmentLength = _items.Length - _head;
        Array.Copy(_items, _head, destination, 0, firstSegmentLength);
        Array.Copy(_items, 0, destination, firstSegmentLength, Count - firstSegmentLength);
    }

    private void ClearPhysicalRange(int physicalIndex, int count)
    {
        if (physicalIndex + count <= _items.Length)
        {
            Array.Clear(_items, physicalIndex, count);
            return;
        }

        int firstSegmentLength = _items.Length - physicalIndex;
        Array.Clear(_items, physicalIndex, firstSegmentLength);
        Array.Clear(_items, 0, count - firstSegmentLength);
    }

    private int PhysicalIndex(int logicalIndex) => (_head + logicalIndex) % _items.Length;
}

/// <summary>
/// Screen buffer holding a grid of terminal cells with optional scrollback.
/// Supports virtualized access for large scroll buffers.
/// </summary>
public sealed class TerminalScreen
{
    private TerminalRowBuffer _rows;
    private TerminalRowBuffer? _primaryRows;
    private TerminalRowBuffer? _alternateRows;
    private readonly Dictionary<int, string> _hyperlinksById = [];
    private readonly Dictionary<string, int> _hyperlinkIdsByUrl = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TerminalKittyImageSource> _kittyImagesById = [];
    private Dictionary<int, TerminalRasterImageSource> _rasterImagesById = [];
    private List<TerminalRasterImagePlacement> _rasterPlacements = [];
    private Dictionary<int, TerminalRasterImageSource>? _primaryRasterImagesById;
    private List<TerminalRasterImagePlacement>? _primaryRasterPlacements;
    private Dictionary<int, TerminalRasterImageSource>? _alternateRasterImagesById;
    private List<TerminalRasterImagePlacement>? _alternateRasterPlacements;
    private TerminalKittyImagePlacement[] _kittyPlacements = Array.Empty<TerminalKittyImagePlacement>();
    private int _nextHyperlinkId = 1;
    private int _nextRasterImageId = 1;
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

    /// <summary>Maximum number of primary-buffer scrollback rows.</summary>
    public int ScrollbackLimit
    {
        get => _scrollbackLimit;
        set
        {
            int normalizedValue = NormalizeScrollbackLimit(value);
            if (_scrollbackLimit == normalizedValue)
            {
                return;
            }

            _scrollbackLimit = normalizedValue;
            if (!_alternateBufferActive)
            {
                TrimScrollbackRows();
            }

            ScrollOffset = _viewportTop;
        }
    }

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

    /// <summary>Gets whether the current screen includes protocol-neutral raster image placements.</summary>
    public bool HasRasterGraphics => _rasterPlacements.Count > 0;

    /// <summary>Gets the absolute row index of the first viewport row.</summary>
    public int ViewportTopAbsoluteRow => GetAbsoluteRowForViewportRow(0);

    public TerminalScreen(int columns, int viewportRows, int scrollbackLimit = 10_000)
    {
        Columns = columns;
        ViewportRows = viewportRows;
        _scrollbackLimit = NormalizeScrollbackLimit(scrollbackLimit);
        _rows = new TerminalRowBuffer(viewportRows);
        _theme = _theme
            .WithDefaultForeground(DefaultForeground)
            .WithDefaultBackground(DefaultBackground)
            .WithCursorColor(DefaultForeground);

        // Initialize visible rows
        for (var i = 0; i < viewportRows; i++)
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
    }

    private static int NormalizeScrollbackLimit(int scrollbackLimit)
    {
        return Math.Max(0, scrollbackLimit);
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

    private static void RemapRowColors(TerminalRowBuffer rows, IReadOnlyDictionary<uint, uint> colorRemap)
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
    /// Gets the absolute row index for a row relative to the viewport.
    /// </summary>
    public int GetAbsoluteRowForViewportRow(int viewportRow)
    {
        if (_rows.Count == 0)
        {
            return -1;
        }

        int clampedViewportRow = Math.Clamp(viewportRow, 0, Math.Max(0, ViewportRows - 1));
        int absoluteRow = TotalRows - ViewportRows - _viewportTop + clampedViewportRow;
        return Math.Clamp(absoluteRow, 0, TotalRows - 1);
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
        if (imageId == 0)
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

    /// <summary>Allocates a stable id for a protocol-neutral raster image.</summary>
    public int AllocateRasterImageId()
    {
        int nextId = _nextRasterImageId;
        if (nextId == int.MaxValue)
        {
            nextId = 1;
            while (_rasterImagesById.ContainsKey(nextId))
            {
                nextId++;
            }
        }

        _nextRasterImageId = nextId + 1;
        return nextId;
    }

    /// <summary>Gets the current protocol-neutral raster image placement snapshot.</summary>
    public ReadOnlySpan<TerminalRasterImagePlacement> GetRasterImagePlacements()
        => CollectionsMarshal.AsSpan(_rasterPlacements);

    /// <summary>Attempts to resolve a protocol-neutral raster image payload by image id.</summary>
    public bool TryGetRasterImageSource(int imageId, out TerminalRasterImageSource? source)
    {
        if (imageId <= 0)
        {
            source = null;
            return false;
        }

        return _rasterImagesById.TryGetValue(imageId, out source);
    }

    /// <summary>Adds or replaces a protocol-neutral raster image and placement.</summary>
    public void ReplaceRasterImage(TerminalRasterImageSource source, TerminalRasterImagePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(placement);

        if (source.ImageId != placement.ImageId)
        {
            throw new ArgumentException("Raster image source and placement ids must match.", nameof(placement));
        }

        int startAbsRow = GetRasterPlacementStartRow(placement);
        int endAbsRow = GetRasterPlacementEndRow(placement);
        int startColumn = GetRasterPlacementStartColumn(placement);
        int endColumn = GetRasterPlacementEndColumn(placement);
        bool removedExisting = false;
        for (int i = _rasterPlacements.Count - 1; i >= 0; i--)
        {
            TerminalRasterImagePlacement existingPlacement = _rasterPlacements[i];
            if (existingPlacement.ImageId == source.ImageId ||
                RasterPlacementIntersects(existingPlacement, startAbsRow, endAbsRow, startColumn, endColumn))
            {
                _rasterPlacements.RemoveAt(i);
                removedExisting = true;
            }
        }

        if (removedExisting)
        {
            TrimUnreferencedRasterSources();
        }

        _rasterImagesById[source.ImageId] = source;
        ClearTextContentUnderRasterPlacement(placement);
        _rasterPlacements.Add(placement);
        InvalidateViewport();
    }

    /// <summary>Replaces protocol-neutral raster graphics from another screen snapshot.</summary>
    public void ReplaceRasterGraphicsFrom(TerminalScreen source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source._rasterImagesById.Count == 0 && source._rasterPlacements.Count == 0)
        {
            ClearRasterGraphics();
            return;
        }

        _rasterImagesById.Clear();
        foreach ((int imageId, TerminalRasterImageSource image) in source._rasterImagesById)
        {
            _rasterImagesById[imageId] = image;
        }

        _rasterPlacements.Clear();
        _rasterPlacements.AddRange(source._rasterPlacements);
        ClearTextContentUnderRasterPlacements();
        InvalidateViewport();
    }

    /// <summary>Clears all protocol-neutral raster images from the active screen.</summary>
    public void ClearRasterGraphics()
    {
        if (_rasterImagesById.Count == 0 && _rasterPlacements.Count == 0)
        {
            return;
        }

        _rasterImagesById.Clear();
        _rasterPlacements.Clear();
        InvalidateViewport();
    }

    /// <summary>Clears raster images intersecting a viewport rectangle.</summary>
    public void ClearRasterGraphicsInViewportRectangle(
        int startViewportRow,
        int endViewportRow,
        int startColumn,
        int endColumn)
    {
        if (_rasterPlacements.Count == 0)
        {
            return;
        }

        int startRow = Math.Clamp(Math.Min(startViewportRow, endViewportRow), 0, Math.Max(0, ViewportRows - 1));
        int endRow = Math.Clamp(Math.Max(startViewportRow, endViewportRow), 0, Math.Max(0, ViewportRows - 1));
        int startAbsRow = GetAbsoluteRowForViewportRow(startRow);
        int endAbsRow = GetAbsoluteRowForViewportRow(endRow);
        int minColumn = Math.Clamp(Math.Min(startColumn, endColumn), 0, Math.Max(0, Columns - 1));
        int maxColumn = Math.Clamp(Math.Max(startColumn, endColumn), 0, Math.Max(0, Columns - 1));

        bool removed = false;
        for (int i = _rasterPlacements.Count - 1; i >= 0; i--)
        {
            TerminalRasterImagePlacement placement = _rasterPlacements[i];
            if (RasterPlacementIntersects(placement, startAbsRow, endAbsRow, minColumn, maxColumn))
            {
                _rasterPlacements.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            TrimUnreferencedRasterSources();
            InvalidateViewport();
        }
    }

    /// <summary>Shifts raster image anchors inside a viewport row range.</summary>
    public void ShiftRasterGraphicsInViewportRows(int startViewportRow, int endViewportRow, int rowDelta)
    {
        if (_rasterPlacements.Count == 0 || rowDelta == 0)
        {
            return;
        }

        int startRow = Math.Clamp(Math.Min(startViewportRow, endViewportRow), 0, Math.Max(0, ViewportRows - 1));
        int endRow = Math.Clamp(Math.Max(startViewportRow, endViewportRow), 0, Math.Max(0, ViewportRows - 1));
        int startAbsRow = GetAbsoluteRowForViewportRow(startRow);
        int endAbsRow = GetAbsoluteRowForViewportRow(endRow);
        bool changed = false;

        for (int i = _rasterPlacements.Count - 1; i >= 0; i--)
        {
            TerminalRasterImagePlacement placement = _rasterPlacements[i];
            if (!RasterPlacementIntersectsRows(placement, startAbsRow, endAbsRow))
            {
                continue;
            }

            int nextAnchorRow = placement.AnchorRow + rowDelta;
            TerminalRasterImagePlacement shifted = placement.WithAnchorRow(nextAnchorRow);
            if (!RasterPlacementIntersectsRows(shifted, startAbsRow, endAbsRow))
            {
                _rasterPlacements.RemoveAt(i);
            }
            else
            {
                _rasterPlacements[i] = shifted;
            }

            changed = true;
        }

        if (changed)
        {
            TrimUnreferencedRasterSources();
            InvalidateViewport();
        }
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
        int removedRows = 0;
        int overflowRows = _rows.Count - maxRows;
        if (overflowRows > 0)
        {
            _rows.RemoveFirst(overflowRows);
            removedRows = overflowRows;
        }

        if (removedRows > 0)
        {
            ShiftRasterGraphicsAfterTopRowsRemoved(removedRows);
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
                ClearRasterGraphics();
            }

            InvalidateAll();
            return;
        }

        _primaryRows = _rows;
        _primaryScrollOffset = _viewportTop;
        _primaryRasterImagesById = _rasterImagesById;
        _primaryRasterPlacements = _rasterPlacements;
        _alternateBufferActive = true;
        _viewportTop = 0;

        _alternateRows ??= new TerminalRowBuffer(ViewportRows);
        _rows = _alternateRows;
        _rasterImagesById = _alternateRasterImagesById ?? [];
        _rasterPlacements = _alternateRasterPlacements ?? [];
        EnsureAlternateRows();

        if (clear)
        {
            ClearActiveRows();
            ClearRasterGraphics();
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
        _alternateRasterImagesById = _rasterImagesById;
        _alternateRasterPlacements = _rasterPlacements;
        _rows = _primaryRows ?? CreateRows(Columns, ViewportRows, DefaultForeground, DefaultBackground);
        _rasterImagesById = _primaryRasterImagesById ?? [];
        _rasterPlacements = _primaryRasterPlacements ?? [];
        _primaryRows = null;
        _primaryRasterImagesById = null;
        _primaryRasterPlacements = null;
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
        _alternateRasterImagesById = null;
        _alternateRasterPlacements = null;
    }

    /// <summary>
    /// Appends transient blank rows until the bottom-anchored viewport starts at or after the requested absolute row.
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
            AddTransientResizeRow();
        }

        ScrollOffset = 0;
    }

    /// <summary>
    /// Clears primary-buffer scrollback while preserving the active viewport.
    /// </summary>
    public void ClearScrollback()
    {
        if (_alternateBufferActive)
        {
            EnsureAlternateRows();
            ScrollOffset = 0;
            InvalidateViewport();
            return;
        }

        int scrollbackRows = Math.Max(0, _rows.Count - ViewportRows);
        if (scrollbackRows <= 0)
        {
            ScrollOffset = 0;
            return;
        }

        TerminalRowBuffer rows = new(ViewportRows);
        int firstViewportRow = Math.Max(0, _rows.Count - ViewportRows);
        for (int rowIndex = firstViewportRow; rowIndex < _rows.Count; rowIndex++)
        {
            rows.Add(_rows[rowIndex]);
        }

        _rows = rows;
        EnsureMinimumRows(ViewportRows);
        ShiftRasterGraphicsAfterTopRowsRemoved(scrollbackRows);
        ScrollOffset = 0;
        InvalidateAll();
    }

    /// <summary>
    /// Clears primary-buffer scrollback and makes the active cursor line the first viewport row.
    /// </summary>
    /// <param name="cursorViewportRow">The cursor row in viewport coordinates.</param>
    public void ClearVisibleHistory(int cursorViewportRow)
    {
        if (_alternateBufferActive)
        {
            EnsureAlternateRows();
            ScrollOffset = 0;
            InvalidateViewport();
            return;
        }

        ClearScrollback();

        int cursorRow = Math.Clamp(cursorViewportRow, 0, Math.Max(0, ViewportRows - 1));
        TerminalRow sourceRow = GetViewportRow(cursorRow);
        TerminalRow promptRow = new(Columns, DefaultForeground, DefaultBackground);
        promptRow.CopyActiveFrom(sourceRow, DefaultForeground, DefaultBackground);
        promptRow.WrapsToNext = false;

        TerminalRowBuffer rows = new(ViewportRows);
        rows.Add(promptRow);
        for (int rowIndex = 1; rowIndex < ViewportRows; rowIndex++)
        {
            rows.Add(new TerminalRow(Columns, DefaultForeground, DefaultBackground));
        }

        _rows = rows;
        ClearRasterGraphics();
        ClearKittyGraphics();
        ScrollOffset = 0;
        InvalidateAll();
    }

    /// <summary>
    /// Clears the active buffer and all history, returning the primary buffer to blank viewport rows.
    /// </summary>
    public void ClearAll()
    {
        if (_alternateBufferActive)
        {
            SwitchToPrimaryBuffer();
        }

        _rows = CreateRows(Columns, ViewportRows, DefaultForeground, DefaultBackground);
        _primaryRows = null;
        _alternateRows = null;
        _primaryRasterImagesById = null;
        _primaryRasterPlacements = null;
        _alternateRasterImagesById = null;
        _alternateRasterPlacements = null;
        _primaryScrollOffset = 0;
        _viewportTop = 0;
        ClearHyperlinks();
        ClearRasterGraphics();
        ClearKittyGraphics();
        InvalidateAll();
    }

    /// <summary>
    /// Moves the non-empty active viewport into scrollback and clears the active viewport.
    /// </summary>
    public void MoveViewportToScrollbackAndClear()
    {
        if (_alternateBufferActive)
        {
            EnsureAlternateRows();
            ClearActiveRows();
            ClearRasterGraphics();
            ClearKittyGraphics();
            ScrollOffset = 0;
            InvalidateAll();
            return;
        }

        DiscardTransientResizeRows();
        ScrollOffset = 0;

        AppendBlankRowsAndTrimScrollback(GetNonEmptyViewportRowCount());

        ClearViewportRows();
        ClearRasterGraphicsInViewportRectangle(
            0,
            Math.Max(0, ViewportRows - 1),
            0,
            Math.Max(0, Columns - 1));
        ClearKittyGraphics();
        ScrollOffset = 0;
        InvalidateAll();
    }

    /// <summary>
    /// Removes trailing rows that were appended only to preserve a Windows PTY viewport during resize.
    /// </summary>
    /// <returns>The number of transient resize rows removed.</returns>
    public int DiscardTransientResizeRows()
    {
        if (_alternateBufferActive || _rows.Count == 0)
        {
            return 0;
        }

        int removableRows = 0;
        for (int rowIndex = _rows.Count - 1; rowIndex >= 0; rowIndex--)
        {
            TerminalRow row = _rows[rowIndex];
            if (!row.IsTransientResizeRow || RowHasContent(row))
            {
                break;
            }

            removableRows++;
        }

        if (removableRows == 0)
        {
            return 0;
        }

        _rows.RemoveRange(_rows.Count - removableRows, removableRows);
        ScrollOffset = 0;
        InvalidateAll();
        return removableRows;
    }

    private static bool RowHasContent(TerminalRow row)
    {
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        for (int column = 0; column < cells.Length; column++)
        {
            TerminalCell cell = cells[column];
            if (cell.Width != 0 && cell.HasContent)
            {
                return true;
            }
        }

        return false;
    }

    private int GetNonEmptyViewportRowCount()
    {
        for (int viewportRow = ViewportRows - 1; viewportRow >= 0; viewportRow--)
        {
            if (RowHasContent(GetViewportRow(viewportRow)) || RowHasRasterContent(viewportRow))
            {
                return viewportRow + 1;
            }
        }

        return 0;
    }

    private bool RowHasRasterContent(int viewportRow)
    {
        if (_rasterPlacements.Count == 0)
        {
            return false;
        }

        int absoluteRow = GetAbsoluteRowForViewportRow(viewportRow);
        for (int i = 0; i < _rasterPlacements.Count; i++)
        {
            if (RasterPlacementIntersectsRows(_rasterPlacements[i], absoluteRow, absoluteRow))
            {
                return true;
            }
        }

        return false;
    }

    private void ClearHyperlinks()
    {
        _hyperlinksById.Clear();
        _hyperlinkIdsByUrl.Clear();
        _nextHyperlinkId = 1;
    }

    /// <summary>
    /// Drops cells retained outside each active row width after an external backend resize.
    /// </summary>
    public void DiscardHiddenCells()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            TerminalRow row = _rows[i];
            if (row.PreservedColumns > row.Columns)
            {
                row.ClearPreservedCellsFrom(row.Columns, DefaultForeground, DefaultBackground);
            }
        }
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

    private void ClearViewportRows()
    {
        for (int rowIndex = 0; rowIndex < ViewportRows; rowIndex++)
        {
            GetViewportRow(rowIndex).Clear(DefaultForeground, DefaultBackground);
        }
    }

    private void AppendBlankRowsAndTrimScrollback(int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }

        int maxRows = ViewportRows + (_alternateBufferActive ? 0 : _scrollbackLimit);
        int overflowRows = Math.Max(0, _rows.Count + rowCount - maxRows);
        if (overflowRows > 0)
        {
            _rows.RemoveFirst(overflowRows);
            ShiftRasterGraphicsAfterTopRowsRemoved(overflowRows);
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            _rows.Add(new TerminalRow(Columns, DefaultForeground, DefaultBackground));
        }
    }

    private TerminalRow AddTransientResizeRow()
    {
        TerminalRow row = AddRow();
        row.IsTransientResizeRow = true;
        return row;
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

    private void ShiftRasterGraphicsAfterTopRowsRemoved(int removedRows)
    {
        if (_rasterPlacements.Count == 0 || removedRows <= 0)
        {
            return;
        }

        bool changed = false;
        for (int i = _rasterPlacements.Count - 1; i >= 0; i--)
        {
            TerminalRasterImagePlacement placement = _rasterPlacements[i].WithAnchorRow(
                _rasterPlacements[i].AnchorRow - removedRows);
            if (GetRasterPlacementEndRow(placement) < 0)
            {
                _rasterPlacements.RemoveAt(i);
            }
            else
            {
                _rasterPlacements[i] = placement;
            }

            changed = true;
        }

        if (changed)
        {
            TrimUnreferencedRasterSources();
        }
    }

    private void TrimUnreferencedRasterSources()
    {
        if (_rasterImagesById.Count == 0)
        {
            return;
        }

        HashSet<int> activeIds = new();
        for (int i = 0; i < _rasterPlacements.Count; i++)
        {
            activeIds.Add(_rasterPlacements[i].ImageId);
        }

        List<int>? staleIds = null;
        foreach (int imageId in _rasterImagesById.Keys)
        {
            if (!activeIds.Contains(imageId))
            {
                staleIds ??= [];
                staleIds.Add(imageId);
            }
        }

        if (staleIds is null)
        {
            return;
        }

        for (int i = 0; i < staleIds.Count; i++)
        {
            _rasterImagesById.Remove(staleIds[i]);
        }
    }

    private static bool RasterPlacementIntersects(
        TerminalRasterImagePlacement placement,
        int startAbsRow,
        int endAbsRow,
        int startColumn,
        int endColumn)
    {
        return RasterPlacementIntersectsRows(placement, startAbsRow, endAbsRow) &&
            GetRasterPlacementStartColumn(placement) <= endColumn &&
            GetRasterPlacementEndColumn(placement) >= startColumn;
    }

    private static bool RasterPlacementIntersectsRows(
        TerminalRasterImagePlacement placement,
        int startAbsRow,
        int endAbsRow)
    {
        return placement.AnchorRow <= endAbsRow &&
            GetRasterPlacementEndRow(placement) >= startAbsRow;
    }

    private static int GetRasterPlacementStartColumn(TerminalRasterImagePlacement placement)
    {
        int leftOffset = Math.Min(0, placement.XOffsetPx);
        int leftCells = FloorDiv(leftOffset, placement.CellWidthPx);
        return placement.AnchorColumn + leftCells;
    }

    private static int GetRasterPlacementEndColumn(TerminalRasterImagePlacement placement)
    {
        int rightPx = placement.XOffsetPx + Math.Max(0, placement.WidthPx) - 1;
        if (rightPx < 0)
        {
            return placement.AnchorColumn;
        }

        return placement.AnchorColumn + (rightPx / placement.CellWidthPx);
    }

    private static int GetRasterPlacementEndRow(TerminalRasterImagePlacement placement)
    {
        int bottomPx = placement.YOffsetPx + Math.Max(0, placement.HeightPx) - 1;
        if (bottomPx < 0)
        {
            return placement.AnchorRow;
        }

        return placement.AnchorRow + (bottomPx / placement.CellHeightPx);
    }

    private static int GetRasterPlacementStartRow(TerminalRasterImagePlacement placement)
    {
        int topOffset = Math.Min(0, placement.YOffsetPx);
        int topCells = FloorDiv(topOffset, placement.CellHeightPx);
        return placement.AnchorRow + topCells;
    }

    private void ClearTextContentUnderRasterPlacements()
    {
        for (int i = 0; i < _rasterPlacements.Count; i++)
        {
            ClearTextContentUnderRasterPlacement(_rasterPlacements[i]);
        }
    }

    private void ClearTextContentUnderRasterPlacement(TerminalRasterImagePlacement placement)
    {
        int startAbsRow = Math.Max(0, GetRasterPlacementStartRow(placement));
        int endAbsRow = Math.Min(_rows.Count - 1, GetRasterPlacementEndRow(placement));
        if (startAbsRow > endAbsRow)
        {
            return;
        }

        int startColumn = Math.Clamp(GetRasterPlacementStartColumn(placement), 0, Math.Max(0, Columns - 1));
        int endColumn = Math.Clamp(GetRasterPlacementEndColumn(placement), 0, Math.Max(0, Columns - 1));
        if (startColumn > endColumn)
        {
            return;
        }

        for (int rowIndex = startAbsRow; rowIndex <= endAbsRow; rowIndex++)
        {
            TerminalRow row = _rows[rowIndex];
            if (row.Columns == 0)
            {
                continue;
            }

            int rowStart = Math.Min(startColumn, row.Columns - 1);
            int rowEnd = Math.Min(endColumn, row.Columns - 1);
            if (rowStart > 0 && row[rowStart].Width == 0)
            {
                rowStart--;
            }

            if (rowEnd + 1 < row.Columns && row[rowEnd].Width == 2)
            {
                rowEnd++;
            }

            for (int column = rowStart; column <= rowEnd; column++)
            {
                ClearCellTextPreservingColors(ref row[column]);
            }

            row.IsDirty = true;
        }
    }

    private static void ClearCellTextPreservingColors(ref TerminalCell cell)
    {
        uint foreground = cell.Foreground;
        uint background = cell.Background;
        bool hasBackground = cell.HasBackground;

        cell = TerminalCell.Empty(foreground, background);
        cell.HasBackground = hasBackground;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder != 0 && ((remainder < 0) != (divisor < 0))
            ? quotient - 1
            : quotient;
    }

    private void TrimScrollbackRows()
    {
        int removedRows = 0;
        int overflowRows = _rows.Count - (ViewportRows + _scrollbackLimit);
        if (overflowRows > 0)
        {
            _rows.RemoveFirst(overflowRows);
            removedRows = overflowRows;
        }

        if (removedRows > 0)
        {
            ShiftRasterGraphicsAfterTopRowsRemoved(removedRows);
        }
    }

    private static TerminalRowBuffer CreateRows(int columns, int rows, uint defaultForeground, uint defaultBackground)
    {
        TerminalRowBuffer result = new(rows);
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
        TerminalGridPosition? trackedViewportPosition = null,
        bool preserveViewportTopOnRowsIncrease = false)
    {
        return Resize(
            columns,
            viewportRows,
            reflowOnResize,
            trackedViewportPosition,
            Span<TerminalGridPosition>.Empty,
            preserveViewportTopOnRowsIncrease);
    }

    /// <summary>
    /// Resizes the screen to new dimensions and maps absolute grid anchors through row reflow.
    /// </summary>
    /// <param name="columns">The new column count.</param>
    /// <param name="viewportRows">The new viewport row count.</param>
    /// <param name="reflowOnResize">Whether existing rows should be reflowed when the width changes.</param>
    /// <param name="trackedViewportPosition">Optional viewport-relative position to map and return.</param>
    /// <param name="trackedAbsolutePositions">
    /// Absolute row positions to map in place. The <see cref="TerminalGridPosition.Row"/> value is treated as an
    /// absolute row before resizing and is replaced with the mapped absolute row after resizing.
    /// </param>
    /// <param name="preserveViewportTopOnRowsIncrease">
    /// Whether Windows PTY-style resizes should keep the old live viewport top in scrollback by appending blank
    /// rows when reflow or row growth would otherwise pull older scrollback rows into the active viewport.
    /// Windows ConPTY needs this because the host repaints the active viewport after resize.
    /// </param>
    public TerminalGridPosition Resize(
        int columns,
        int viewportRows,
        bool reflowOnResize,
        TerminalGridPosition? trackedViewportPosition,
        Span<TerminalGridPosition> trackedAbsolutePositions,
        bool preserveViewportTopOnRowsIncrease = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewportRows, 1);

        int oldColumns = Columns;
        int oldViewportRows = ViewportRows;
        bool preserveLiveViewportTop =
            preserveViewportTopOnRowsIncrease &&
            !_alternateBufferActive &&
            _viewportTop == 0 &&
            _rows.Count >= oldViewportRows;
        // Capture before dropping transient bottom padding. Height-only resizes
        // after a previous grow would otherwise move the bottom-anchored live
        // viewport and cause repaint rows to land on the wrong backing rows.
        int oldLiveViewportTopAbsoluteRow = preserveLiveViewportTop
            ? Math.Max(0, _rows.Count - oldViewportRows)
            : -1;

        if (preserveViewportTopOnRowsIncrease)
        {
            DiscardTransientResizeRows();
        }

        bool shouldAppendRowsForViewportGrowth =
            preserveLiveViewportTop &&
            viewportRows > oldViewportRows &&
            _rows.Count >= oldViewportRows;
        int rowsToAppendForViewportGrowth = shouldAppendRowsForViewportGrowth
            ? Math.Min(viewportRows - oldViewportRows, Math.Max(0, _rows.Count - oldViewportRows))
            : 0;
        List<TerminalRasterImagePlacement>? reflowRasterPlacements = null;
        List<ReflowAnchorPosition>? reflowAnchors = null;
        int trackedAbsolutePositionCount = trackedAbsolutePositions.Length;
        int liveViewportTopAnchorIndex = -1;
        if (columns != oldColumns &&
            reflowOnResize &&
            (trackedAbsolutePositionCount > 0 || preserveLiveViewportTop || _rasterPlacements.Count > 0))
        {
            if (_rasterPlacements.Count > 0)
            {
                reflowRasterPlacements = new List<TerminalRasterImagePlacement>(_rasterPlacements);
            }

            reflowAnchors = new List<ReflowAnchorPosition>(
                trackedAbsolutePositionCount +
                (preserveLiveViewportTop ? 1 : 0) +
                _rasterPlacements.Count);
            for (int i = 0; i < trackedAbsolutePositionCount; i++)
            {
                TerminalGridPosition trackedAbsolutePosition = trackedAbsolutePositions[i];
                reflowAnchors.Add(new ReflowAnchorPosition(
                    trackedAbsolutePosition.Row,
                    trackedAbsolutePosition.Column,
                    allowEndColumn: true,
                    extendLineToColumn: false));
            }

            if (preserveLiveViewportTop)
            {
                liveViewportTopAnchorIndex = reflowAnchors.Count;
                reflowAnchors.Add(new ReflowAnchorPosition(
                    oldLiveViewportTopAbsoluteRow,
                    oldColumn: 0,
                    allowEndColumn: true,
                    extendLineToColumn: false));
            }

            for (int i = 0; i < _rasterPlacements.Count; i++)
            {
                TerminalRasterImagePlacement placement = _rasterPlacements[i];
                reflowAnchors.Add(new ReflowAnchorPosition(
                    placement.AnchorRow,
                    placement.AnchorColumn));
            }
        }

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
                ReflowRows(
                    columns,
                    trackedAbsoluteRow,
                    trackedColumn,
                    reflowAnchors,
                    out mappedAbsoluteRow,
                    out mappedColumn);
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

        for (int i = 0; i < rowsToAppendForViewportGrowth; i++)
        {
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground)
            {
                IsTransientResizeRow = true,
            });
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
            int overflowRows = _rows.Count - (ViewportRows + _scrollbackLimit);
            if (overflowRows > 0)
            {
                _rows.RemoveFirst(overflowRows);
                removedRows = overflowRows;
            }
        }

        if (mappedAbsoluteRow >= 0)
        {
            mappedAbsoluteRow -= removedRows;
        }

        int mappedLiveViewportTopAbsoluteRow = -1;
        if (preserveLiveViewportTop)
        {
            if (liveViewportTopAnchorIndex >= 0 &&
                reflowAnchors is not null &&
                liveViewportTopAnchorIndex < reflowAnchors.Count)
            {
                ReflowAnchorPosition mappedAnchor = reflowAnchors[liveViewportTopAnchorIndex];
                mappedLiveViewportTopAbsoluteRow = mappedAnchor.IsMapped
                    ? mappedAnchor.NewAbsoluteRow
                    : mappedAnchor.OldAbsoluteRow;
            }
            else
            {
                mappedLiveViewportTopAbsoluteRow = oldLiveViewportTopAbsoluteRow;
            }

            mappedLiveViewportTopAbsoluteRow = Math.Max(0, mappedLiveViewportTopAbsoluteRow - removedRows);
        }

        int rasterAnchorOffset = trackedAbsolutePositionCount + (liveViewportTopAnchorIndex >= 0 ? 1 : 0);
        if (reflowRasterPlacements is not null && reflowAnchors is not null)
        {
            RemapTrackedAbsolutePositionsAfterReflow(trackedAbsolutePositions, reflowAnchors, removedRows);
            RemapRasterGraphicsAfterReflow(
                reflowRasterPlacements,
                reflowAnchors,
                rasterAnchorOffset,
                removedRows);
        }
        else if (reflowAnchors is not null)
        {
            RemapTrackedAbsolutePositionsAfterReflow(trackedAbsolutePositions, reflowAnchors, removedRows);
        }
        else if (removedRows > 0)
        {
            RemapTrackedAbsolutePositionsWithoutReflow(trackedAbsolutePositions, columns, removedRows);
            ShiftRasterGraphicsAfterTopRowsRemoved(removedRows);
        }
        else
        {
            RemapTrackedAbsolutePositionsWithoutReflow(trackedAbsolutePositions, columns, removedRows);
        }

        if (mappedLiveViewportTopAbsoluteRow >= 0)
        {
            int minimumViewportTopAbsoluteRow = CalculateWindowsPtyResizeViewportTop(
                oldColumns,
                columns,
                viewportRows,
                mappedLiveViewportTopAbsoluteRow,
                mappedAbsoluteRow);
            PadBottomViewportToPreserveTop(minimumViewportTopAbsoluteRow);
        }

        ScrollOffset = _viewportTop;

        foreach (TerminalRow row in _rows)
        {
            row.IsDirty = true;
        }

        return GetViewportPositionForAbsoluteRow(mappedAbsoluteRow, mappedColumn);
    }

    private int CalculateWindowsPtyResizeViewportTop(
        int oldColumns,
        int newColumns,
        int viewportRows,
        int mappedLiveViewportTopAbsoluteRow,
        int mappedCursorAbsoluteRow)
    {
        int lastContentRow = GetLastContentRowIndex();
        int maxRow = mappedCursorAbsoluteRow >= 0
            ? Math.Max(lastContentRow, mappedCursorAbsoluteRow)
            : lastContentRow;
        int proposedTopFromLastLine = maxRow - viewportRows + 1;
        int proposedTopFromScrollback = mappedLiveViewportTopAbsoluteRow;
        int proposedTop = Math.Max(proposedTopFromLastLine, proposedTopFromScrollback);

        if (proposedTop == proposedTopFromScrollback)
        {
            int proposedBottom = proposedTopFromScrollback + viewportRows - 1;
            if (maxRow < proposedBottom &&
                newColumns < oldColumns &&
                proposedTop > 0 &&
                proposedTop - 1 < _rows.Count &&
                _rows[proposedTop - 1].WrapsToNext)
            {
                proposedTop--;
            }
        }

        int proposedScrollbackBottom = proposedTopFromScrollback + viewportRows - 1;
        if (maxRow > proposedScrollbackBottom)
        {
            proposedTop = proposedTopFromLastLine;
        }

        proposedTop = Math.Max(0, proposedTop);
        if (mappedCursorAbsoluteRow >= 0)
        {
            proposedTop = Math.Min(proposedTop, mappedCursorAbsoluteRow);
        }

        return proposedTop;
    }

    private int GetLastContentRowIndex()
    {
        for (int rowIndex = _rows.Count - 1; rowIndex >= 0; rowIndex--)
        {
            if (GetReflowEndExclusive(_rows[rowIndex]) > 0)
            {
                return rowIndex;
            }
        }

        return 0;
    }

    private void RemapTrackedAbsolutePositionsAfterReflow(
        Span<TerminalGridPosition> trackedAbsolutePositions,
        IReadOnlyList<ReflowAnchorPosition> mappedAnchors,
        int removedRows)
    {
        for (int i = 0; i < trackedAbsolutePositions.Length && i < mappedAnchors.Count; i++)
        {
            ReflowAnchorPosition mappedAnchor = mappedAnchors[i];
            int absoluteRow = mappedAnchor.IsMapped
                ? mappedAnchor.NewAbsoluteRow - removedRows
                : mappedAnchor.OldAbsoluteRow - removedRows;
            int column = mappedAnchor.IsMapped
                ? mappedAnchor.NewColumn
                : mappedAnchor.OldColumn;
            trackedAbsolutePositions[i] = new TerminalGridPosition(
                Math.Clamp(column, 0, Columns),
                Math.Clamp(absoluteRow, 0, Math.Max(0, _rows.Count - 1)));
        }
    }

    private void RemapTrackedAbsolutePositionsWithoutReflow(
        Span<TerminalGridPosition> trackedAbsolutePositions,
        int columns,
        int removedRows)
    {
        for (int i = 0; i < trackedAbsolutePositions.Length; i++)
        {
            TerminalGridPosition position = trackedAbsolutePositions[i];
            trackedAbsolutePositions[i] = new TerminalGridPosition(
                Math.Clamp(position.Column, 0, columns),
                Math.Clamp(position.Row - removedRows, 0, Math.Max(0, _rows.Count - 1)));
        }
    }

    private void ResizeRows(int columns)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Resize(columns, DefaultForeground, DefaultBackground);
        }
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
        List<ReflowAnchorPosition>? additionalTrackedPositions,
        out int mappedAbsoluteRow,
        out int mappedColumn)
    {
        List<TerminalRow> reflowedRows = new(_rows.Count);
        List<TerminalCell> logicalLine = new(Math.Max(Columns, columns));
        ReflowAnchorProcessingIndex[]? trackedPositionIndexes =
            CreateReflowAnchorProcessingOrder(additionalTrackedPositions);
        List<int>? lineTrackedIndexes = trackedPositionIndexes is null ? null : new List<int>();
        List<int>? lineTrackedOffsets = trackedPositionIndexes is null ? null : new List<int>();
        mappedAbsoluteRow = trackedAbsoluteRow;
        mappedColumn = Math.Clamp(trackedColumn, 0, columns);

        int nextTrackedPositionIndex = 0;
        int rowIndex = 0;
        while (rowIndex < _rows.Count)
        {
            logicalLine.Clear();
            lineTrackedIndexes?.Clear();
            lineTrackedOffsets?.Clear();
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

                if (additionalTrackedPositions is not null && trackedPositionIndexes is not null)
                {
                    while (nextTrackedPositionIndex < trackedPositionIndexes.Length)
                    {
                        ReflowAnchorProcessingIndex processingIndex =
                            trackedPositionIndexes[nextTrackedPositionIndex];
                        int positionIndex = processingIndex.Index;
                        ReflowAnchorPosition position = additionalTrackedPositions[positionIndex];
                        if (processingIndex.Row < rowIndex)
                        {
                            nextTrackedPositionIndex++;
                            continue;
                        }

                        if (processingIndex.Row != rowIndex)
                        {
                            break;
                        }

                        int clampedColumn = Math.Clamp(position.OldColumn, 0, row.Columns);
                        int effectiveTrackedColumn = position.ExtendLineToColumn
                            ? clampedColumn
                            : endExclusive == 0
                                ? clampedColumn
                                : Math.Min(clampedColumn, endExclusive);
                        if (position.ExtendLineToColumn)
                        {
                            endExclusive = Math.Max(endExclusive, clampedColumn);
                        }

                        lineTrackedIndexes!.Add(positionIndex);
                        lineTrackedOffsets!.Add(logicalLine.Count + effectiveTrackedColumn);
                        nextTrackedPositionIndex++;
                    }
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

            if (additionalTrackedPositions is not null &&
                lineTrackedIndexes is not null &&
                lineTrackedOffsets is not null)
            {
                for (int i = 0; i < lineTrackedIndexes.Count; i++)
                {
                    TerminalGridPosition mappedAnchorPosition = MapLogicalOffsetToReflowedPosition(
                        logicalLine,
                        columns,
                        lineTrackedOffsets[i]);
                    int positionIndex = lineTrackedIndexes[i];
                    ReflowAnchorPosition position = additionalTrackedPositions[positionIndex];
                    int maxColumn = position.AllowEndColumn ? columns : Math.Max(0, columns - 1);
                    position.NewAbsoluteRow = destinationStartRow + mappedAnchorPosition.Row;
                    position.NewColumn = Math.Clamp(mappedAnchorPosition.Column, 0, maxColumn);
                    position.IsMapped = true;
                    additionalTrackedPositions[positionIndex] = position;
                }
            }
        }

        _rows.Clear();
        _rows.AddRange(reflowedRows);
    }

    private static ReflowAnchorProcessingIndex[]? CreateReflowAnchorProcessingOrder(
        List<ReflowAnchorPosition>? additionalTrackedPositions)
    {
        if (additionalTrackedPositions is not { Count: > 0 })
        {
            return null;
        }

        ReflowAnchorProcessingIndex[] indexes = new ReflowAnchorProcessingIndex[additionalTrackedPositions.Count];
        for (int i = 0; i < additionalTrackedPositions.Count; i++)
        {
            indexes[i] = new ReflowAnchorProcessingIndex(i, additionalTrackedPositions[i].OldAbsoluteRow);
        }

        Array.Sort(indexes, static (left, right) =>
        {
            int rowComparison = left.Row.CompareTo(right.Row);
            return rowComparison != 0
                ? rowComparison
                : left.Index.CompareTo(right.Index);
        });
        return indexes;
    }

    private void RemapRasterGraphicsAfterReflow(
        List<TerminalRasterImagePlacement> originalPlacements,
        List<ReflowAnchorPosition> mappedAnchors,
        int mappedAnchorOffset,
        int removedRows)
    {
        _rasterPlacements.Clear();

        for (int i = 0; i < originalPlacements.Count && i + mappedAnchorOffset < mappedAnchors.Count; i++)
        {
            ReflowAnchorPosition mappedAnchor = mappedAnchors[i + mappedAnchorOffset];
            if (!mappedAnchor.IsMapped)
            {
                continue;
            }

            TerminalRasterImagePlacement remapped = originalPlacements[i].WithAnchor(
                mappedAnchor.NewColumn,
                mappedAnchor.NewAbsoluteRow - removedRows);
            if (GetRasterPlacementEndRow(remapped) < 0 || remapped.AnchorRow >= _rows.Count)
            {
                continue;
            }

            _rasterPlacements.Add(remapped);
        }

        TrimUnreferencedRasterSources();
        InvalidateViewport();
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
            if (IsReflowLineEndCell(in cells[i]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static bool IsReflowLineEndCell(ref readonly TerminalCell cell)
    {
        if (cell.Width == 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(cell.Grapheme))
        {
            return !IsAsciiSpaceGrapheme(cell.Grapheme);
        }

        return cell.Codepoint != 0 && cell.Codepoint != ' ';
    }

    private static bool IsAsciiSpaceGrapheme(string grapheme)
    {
        for (int i = 0; i < grapheme.Length; i++)
        {
            if (grapheme[i] != ' ')
            {
                return false;
            }
        }

        return true;
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
                ? new TerminalGridPosition(Math.Clamp(trackedLogicalOffset, 0, columns), 0)
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

    private static TerminalGridPosition MapLogicalOffsetToReflowedPosition(
        List<TerminalCell> logicalLine,
        int columns,
        int trackedLogicalOffset)
    {
        if (logicalLine.Count == 0)
        {
            return new TerminalGridPosition(Math.Clamp(trackedLogicalOffset, 0, columns), 0);
        }

        int sourceIndex = 0;
        int destinationRow = 0;
        int column = 0;
        while (sourceIndex < logicalLine.Count)
        {
            if (trackedLogicalOffset <= sourceIndex)
            {
                return new TerminalGridPosition(column, destinationRow);
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

                if (trackedLogicalOffset <= sourceIndex)
                {
                    return new TerminalGridPosition(column, destinationRow);
                }

                destinationRow++;
                column = 0;
                continue;
            }

            if (width == 2 && column == columns - 1)
            {
                destinationRow++;
                column = 0;
                continue;
            }

            sourceIndex += sourceStep;
            column += width;

            if (trackedLogicalOffset <= sourceIndex)
            {
                return new TerminalGridPosition(column, destinationRow);
            }

            if (column >= columns)
            {
                destinationRow++;
                column = 0;
            }
        }

        return new TerminalGridPosition(0, Math.Max(0, destinationRow));
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

    private struct ReflowAnchorPosition
    {
        public ReflowAnchorPosition(
            int oldAbsoluteRow,
            int oldColumn,
            bool allowEndColumn = false,
            bool extendLineToColumn = true)
        {
            OldAbsoluteRow = oldAbsoluteRow;
            OldColumn = oldColumn;
            AllowEndColumn = allowEndColumn;
            ExtendLineToColumn = extendLineToColumn;
            NewAbsoluteRow = oldAbsoluteRow;
            NewColumn = oldColumn;
            IsMapped = false;
        }

        public int OldAbsoluteRow { get; }

        public int OldColumn { get; }

        public bool AllowEndColumn { get; }

        public bool ExtendLineToColumn { get; }

        public int NewAbsoluteRow { get; set; }

        public int NewColumn { get; set; }

        public bool IsMapped { get; set; }
    }

    private readonly record struct ReflowAnchorProcessingIndex(int Index, int Row);

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
