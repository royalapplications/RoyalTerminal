// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal cell model.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoyalTerminal.Terminal.Snapshots;
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
    /// <summary>
    /// Optional grapheme text for this cell.
    /// When null, rendering/copy should use <see cref="Codepoint"/>.
    /// </summary>
    public string? Grapheme;

    /// <summary>UTF-32 codepoint for this cell. 0 for empty cells.</summary>
    public int Codepoint;

    /// <summary>Foreground color as packed ARGB.</summary>
    public uint Foreground;

    /// <summary>Background color as packed ARGB.</summary>
    public uint Background;

    /// <summary>Unresolved SGR foreground identity, retained for protocols and state serialization.</summary>
    public TerminalColorIdentity ForegroundIdentity;

    /// <summary>Unresolved SGR background identity, retained independently of the displayed ARGB.</summary>
    public TerminalColorIdentity BackgroundIdentity;

    /// <summary>Unresolved SGR underline identity; default means no explicit underline color.</summary>
    public TerminalColorIdentity UnderlineIdentity;

    /// <summary>Optional explicit underline color as packed ARGB.</summary>
    public uint UnderlineColor;

    /// <summary>Hyperlink token id for OSC8 links. Zero means no hyperlink.</summary>
    public int HyperlinkId;

    /// <summary>Cell attribute flags.</summary>
    public CellAttributes Attributes;

    /// <summary>Underline rendering style for this cell.</summary>
    public TerminalUnderlineStyle UnderlineStyle;

    /// <summary>Whether <see cref="UnderlineColor"/> should be used.</summary>
    public bool HasUnderlineColor;

    /// <summary>Extended decoration flags not represented in <see cref="CellAttributes"/>.</summary>
    public CellDecorations Decorations;

    /// <summary>
    /// Whether this cell has an explicit background set (vs inherited/default background).
    /// </summary>
    public bool HasBackground;

    /// <summary>Number of columns this character spans (1 for normal, 2 for wide/CJK).</summary>
    public byte Width;

    /// <summary>Whether this zero-width cell pads the right edge before a wrapped wide glyph,
    /// rather than being the trailing half of a glyph on this row.</summary>
    public bool IsWideSpacerHead
    {
        readonly get => (_metadata & 1) != 0;
        set => _metadata = (byte)(value ? _metadata | 1 : _metadata & ~1);
    }

    private byte _metadata;

    /// <summary>Whether selective erase must preserve this cell (DEC/ISO character protection).</summary>
    public bool IsProtected
    {
        readonly get => (_metadata & 2) != 0;
        set => _metadata = (byte)(value ? _metadata | 2 : _metadata & ~2);
    }

    /// <summary>Shell-integration classification assigned when this cell was printed.</summary>
    public TerminalSemanticContent SemanticContent
    {
        readonly get => (TerminalSemanticContent)((_metadata >> 2) & 3);
        set => _metadata = (byte)((_metadata & ~12) | (((byte)value & 3) << 2));
    }

    /// <summary>Returns true if this cell has content.</summary>
    public readonly bool HasContent => Codepoint != 0 || !string.IsNullOrEmpty(Grapheme);

    /// <summary>Creates a default empty cell with the given colors.</summary>
    public static TerminalCell Empty(uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E)
        => Empty(fg, bg, default);

    /// <summary>Creates an erased cell retaining the original background color identity.</summary>
    public static TerminalCell Empty(uint fg, uint bg, TerminalColorIdentity backgroundIdentity) => new()
    {
        Codepoint = 0,
        Grapheme = null,
        Foreground = fg,
        Background = bg,
        BackgroundIdentity = backgroundIdentity,
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
    internal RoyalTerminal.Terminal.Snapshots.GhosttySnapshotPageAllocation? SnapshotAllocation { get; set; }
    internal int SnapshotAllocationRow { get; set; }
    internal bool SnapshotAllocationUnmodified { get; set; }
    internal ulong SnapshotMetadataRevision { get; private set; }
    private TerminalCell[] _cells;
    private int _columns;
    private byte _rowMetadata;
    private bool CellsAreShared
    {
        get => (_rowMetadata & 1) != 0;
        set => _rowMetadata = (byte)(value ? _rowMetadata | 1 : _rowMetadata & ~1);
    }

    /// <summary>Whether this row has been modified since last render.</summary>
    public bool IsDirty { get; set; } = true;

    /// <summary>
    /// Whether this row soft-wraps into the following row.
    /// Explicit line feeds keep this false.
    /// </summary>
    public bool WrapsToNext { get; set; }

    /// <summary>
    /// Whether this physical row continues a soft-wrapped predecessor. This is
    /// retained independently of the predecessor, which may leave scrollback.
    /// </summary>
    public bool IsWrapContinuation
    {
        get => (_rowMetadata & 8) != 0;
        set => _rowMetadata = (byte)(value ? _rowMetadata | 8 : _rowMetadata & ~8);
    }

    /// <summary>Shell prompt marker; it can be present even on an otherwise empty row.</summary>
    public TerminalSemanticPrompt SemanticPrompt
    {
        get => (TerminalSemanticPrompt)((_rowMetadata >> 1) & 3);
        set => _rowMetadata = (byte)((_rowMetadata & ~6) | (((byte)value & 3) << 1));
    }

    /// <summary>
    /// True when this row was appended only to preserve a Windows PTY live viewport during resize.
    /// </summary>
    internal bool IsTransientResizeRow { get; set; }

    /// <summary>Number of columns in this row.</summary>
    public int Columns => _columns;

    /// <summary>Number of cells retained in backing storage, including cells hidden by a narrower resize.</summary>
    public int PreservedColumns => _cells.Length;

    /// <summary>Access the cells array as a span.</summary>
    public Span<TerminalCell> Cells
    {
        get
        {
            EnsureWritableCells();
            return _cells.AsSpan(0, _columns);
        }
    }

    /// <summary>Read-only access to cells.</summary>
    public ReadOnlySpan<TerminalCell> ReadOnlyCells => _cells.AsSpan(0, _columns);

    /// <summary>Read-only access to all retained cells, including cells hidden by a narrower resize.</summary>
    public ReadOnlySpan<TerminalCell> ReadOnlyPreservedCells => _cells.AsSpan();

    public TerminalRow(int columns, uint defaultFg = 0xFFD4D4D4, uint defaultBg = 0xFF1E1E1E)
        : this(columns, defaultFg, defaultBg, initialize: true)
    {
    }

    private TerminalRow(int columns, uint defaultFg, uint defaultBg, bool initialize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columns);

        _columns = columns;
        _cells = new TerminalCell[columns];
        if (initialize)
        {
            Clear(defaultFg, defaultBg);
        }
    }

    // Reflow overwrites the live prefix and fills the unused suffix itself.
    internal static TerminalRow CreateForReflow(int columns)
        => new(columns, 0, 0, initialize: false);

    private TerminalRow(TerminalRow source)
    {
        SnapshotAllocation = source.SnapshotAllocation;
        SnapshotAllocationRow = source.SnapshotAllocationRow;
        SnapshotAllocationUnmodified = source.SnapshotAllocationUnmodified;
        SnapshotMetadataRevision = source.SnapshotMetadataRevision;
        _cells = source._cells;
        _columns = source._columns;
        CellsAreShared = source.CellsAreShared = true;
        IsDirty = source.IsDirty;
        WrapsToNext = source.WrapsToNext;
        IsWrapContinuation = source.IsWrapContinuation;
        SemanticPrompt = source.SemanticPrompt;
        IsTransientResizeRow = source.IsTransientResizeRow;
    }

    // Callers hold the screen lock and must not retain writable cell references
    // across this boundary. Subsequent mutations detach only the affected row.
    internal TerminalRow CreateStateCopy() => new(this);

    // A search snapshot pins the backing cells with COW. Comparing storage plus
    // row layout detects mutation independently of renderer dirty acknowledgments.
    internal bool HasSameSearchContent(TerminalRow other) =>
        ReferenceEquals(_cells, other._cells) && _columns == other._columns &&
        WrapsToNext == other.WrapsToNext && IsWrapContinuation == other.IsWrapContinuation;

    internal object SearchStorageIdentity => _cells;

    /// <summary>Access a cell by column index.</summary>
    public ref TerminalCell this[int column]
    {
        get
        {
            EnsureWritableCells();
            return ref _cells[column];
        }
    }

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
        IsWrapContinuation = source.IsWrapContinuation;
        SemanticPrompt = source.SemanticPrompt;
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
        IsWrapContinuation = source.IsWrapContinuation;
        SemanticPrompt = source.SemanticPrompt;
        IsTransientResizeRow = source.IsTransientResizeRow;
        IsDirty = true;
    }

    /// <summary>Swaps equal-width active cell-array ownership for an in-page row shift.</summary>
    internal void SwapActiveStorage(TerminalRow other)
    {
        if (_columns != other._columns || _cells.Length != _columns || other._cells.Length != other._columns)
            throw new InvalidOperationException("Active storage swaps require equal, unhidden widths.");
        // Swap ownership, not payload. Each retained COW reader keeps its arrays;
        // swapping the shared flags with them avoids a copy until a later write.
        (_cells, other._cells) = (other._cells, _cells);
        bool shared = CellsAreShared;
        CellsAreShared = other.CellsAreShared;
        other.CellsAreShared = shared;
        (SemanticPrompt, other.SemanticPrompt) = (other.SemanticPrompt, SemanticPrompt);
        (IsTransientResizeRow, other.IsTransientResizeRow) = (other.IsTransientResizeRow, IsTransientResizeRow);
        SnapshotAllocationUnmodified = other.SnapshotAllocationUnmodified = false;
        SnapshotMetadataRevision = unchecked(SnapshotMetadataRevision + 1);
        other.SnapshotMetadataRevision = unchecked(other.SnapshotMetadataRevision + 1);
        IsDirty = other.IsDirty = true;
    }

    /// <summary>Clears cells retained outside the active width after this row is edited while narrow.</summary>
    public void ClearPreservedCellsFrom(int column, uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E)
    {
        int start = Math.Clamp(column, 0, _cells.Length);
        if (start >= _cells.Length)
        {
            return;
        }

        EnsureWritableCells();

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

    /// <summary>Resolves logical colors across active and retained cells.</summary>
    internal bool ResolveCellColors(TerminalTheme theme)
    {
        bool changed = false;
        for (int col = 0; col < _cells.Length; col++)
        {
            TerminalCell cell = _cells[col];
            uint foreground = Resolve(cell.ForegroundIdentity, cell.Foreground, theme.DefaultForeground);
            uint background = Resolve(cell.BackgroundIdentity, cell.Background, theme.DefaultBackground);
            uint underline = cell.HasUnderlineColor
                ? Resolve(cell.UnderlineIdentity, cell.UnderlineColor, foreground)
                : cell.UnderlineColor;
            if (foreground != cell.Foreground || background != cell.Background || underline != cell.UnderlineColor)
            {
                // Resolve first so cursor-only or unchanged themes do not detach shared rows.
                EnsureWritableCells();
                _cells[col].Foreground = foreground;
                _cells[col].Background = background;
                _cells[col].UnderlineColor = underline;
                changed = true;
            }
        }

        if (changed)
        {
            IsDirty = true;
        }

        return changed;

        uint Resolve(TerminalColorIdentity identity, uint current, uint defaultColor)
            => identity.Kind switch
            {
                TerminalColorKind.Palette => theme.Palette[(int)identity.Value],
                TerminalColorKind.Rgb => current,
                _ => defaultColor,
            };
    }

    /// <summary>Clear all cells to the default state.</summary>
    public void Clear(uint fg = 0xFFD4D4D4, uint bg = 0xFF1E1E1E)
        => Clear(fg, bg, default);

    /// <summary>Clears all cells while retaining the original background color identity.</summary>
    public void Clear(uint fg, uint bg, TerminalColorIdentity backgroundIdentity)
    {
        if (CellsAreShared)
        {
            // Every cell is overwritten, so no old payload needs copying.
            _cells = new TerminalCell[_columns];
            CellsAreShared = false;
        }

        ResizePreservedStorage(_columns, fg, bg);
        for (var i = 0; i < _columns; i++)
            _cells[i] = TerminalCell.Empty(fg, bg, backgroundIdentity);
        WrapsToNext = false;
        IsWrapContinuation = false;
        SemanticPrompt = TerminalSemanticPrompt.None;
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
        SnapshotAllocationUnmodified = false;
        SnapshotMetadataRevision = unchecked(SnapshotMetadataRevision + 1);
        if (columns == _cells.Length)
        {
            EnsureWritableCells();
            return;
        }

        int previousLength = _cells.Length;
        Array.Resize(ref _cells, columns);
        CellsAreShared = false;
        for (int i = previousLength; i < _cells.Length; i++)
        {
            _cells[i] = TerminalCell.Empty(defaultFg, defaultBg);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureWritableCells()
    {
        SnapshotAllocationUnmodified = false;
        SnapshotMetadataRevision = unchecked(SnapshotMetadataRevision + 1);
        if (!CellsAreShared)
        {
            return;
        }

        _cells = (TerminalCell[])_cells.Clone();
        CellsAreShared = false;
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

    /// <summary>Prepends owned rows in oldest-to-newest order without moving existing cells.</summary>
    internal void PrependRange(ReadOnlySpan<TerminalRow> rows)
    {
        if (rows.IsEmpty) return;
        foreach (TerminalRow row in rows) ArgumentNullException.ThrowIfNull(row);
        int count = checked(Count + rows.Length);
        EnsureCapacity(count); // All fallible work precedes mutation.
        int head = (int)(((long)_head + _items.Length - rows.Length % _items.Length) % _items.Length);
        for (int i = 0; i < rows.Length; i++)
            _items[(int)(((long)head + i) % _items.Length)] = rows[i];
        _head = head;
        Count = count;
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

        int nextCapacity = (int)Math.Max(desiredCapacity,
            Math.Min(Array.MaxLength, Math.Max(4L, (long)_items.Length * 2)));

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
public sealed partial class TerminalScreen
{
    private TerminalRowBuffer _rows;
    private TerminalRowBuffer? _primaryRows;
    private TerminalRowBuffer? _alternateRows;
    private Dictionary<int, string> _hyperlinksById = [];
    private Dictionary<string, int> _hyperlinkIdsByUrl = new(StringComparer.Ordinal);
    private TerminalHyperlinkRegistry _hyperlinkIdentities = new();
    private Dictionary<int, TerminalKittyImageSource> _kittyImagesById = [];
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
    public int MaxScrollOffset => _snapshotScrollbackQuota?.MaximumBytes == 0 ? 0 : Math.Max(0, TotalRows - ViewportRows);

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
    public object SyncRoot => Synchronization.SyncRoot;

    /// <summary>Coordinates terminal mutation with pending render demand.</summary>
    public TerminalStateSynchronization Synchronization { get; } = new();

    /// <summary>Gets whether the current viewport snapshot includes Kitty image placements.</summary>
    public bool HasKittyGraphics => !GetKittyPlacements().IsEmpty;

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

    // State copies assign every field from their source; do not allocate a
    // throwaway viewport or palette before replacing it with the copied state.
    private TerminalScreen(TerminalRowBuffer rows)
    {
        _rows = rows;
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

        if (CellThemeColorsChanged(_theme, theme))
        {
            ResolveExistingCellColors(theme);
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

    private static bool CellThemeColorsChanged(TerminalTheme previous, TerminalTheme next)
    {
        if (previous.DefaultForeground != next.DefaultForeground || previous.DefaultBackground != next.DefaultBackground)
            return true;
        if (ReferenceEquals(previous.Palette, next.Palette)) return false;
        for (int i = 0; i < 256; i++)
            if (previous.Palette[i] != next.Palette[i]) return true;
        return false;
    }

    private void ResolveExistingCellColors(TerminalTheme theme)
    {
        ResolveRowColors(_rows, theme);

        if (_primaryRows is not null && !ReferenceEquals(_primaryRows, _rows))
        {
            ResolveRowColors(_primaryRows, theme);
        }

        if (_alternateRows is not null && !ReferenceEquals(_alternateRows, _rows))
        {
            ResolveRowColors(_alternateRows, theme);
        }
    }

    private static void ResolveRowColors(TerminalRowBuffer rows, TerminalTheme theme)
    {
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rows[rowIndex].ResolveCellColors(theme);
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

    /// <summary>
    /// Registers an owned OSC 8 identity without coalescing distinct IDs by URL.
    /// A nonempty explicit ID takes precedence over the numeric implicit ID.
    /// Re-registering an existing identity is allocation-free.
    /// </summary>
    public int RegisterHyperlink(ReadOnlySpan<byte> uri, ReadOnlySpan<byte> explicitId, uint implicitId)
    {
        if (uri.IsEmpty) throw new ArgumentException("A hyperlink URI cannot be empty.", nameof(uri));
        if (_hyperlinkIdentities.TryFind(uri, explicitId, implicitId, out int token)) return token;
        token = _nextHyperlinkId;
        if (token == int.MaxValue)
        {
            token = 1;
            while (_hyperlinksById.ContainsKey(token)) token++;
        }
        TerminalHyperlink value = _hyperlinkIdentities.Add(token, uri, explicitId, implicitId);
        _hyperlinksById.Add(token, value.Uri);
        _nextHyperlinkId = token + 1;
        return token;
    }

    /// <summary>Gets the owned protocol identity, when registered with its original bytes.</summary>
    public bool TryGetHyperlink(int hyperlinkId, out TerminalHyperlink? hyperlink)
        => _hyperlinkIdentities.TryGet(hyperlinkId, out hyperlink);

    /// <summary>Gets the current Kitty image placement snapshot.</summary>
    public ReadOnlySpan<TerminalKittyImagePlacement> GetKittyPlacements()
    {
        RefreshKittyProjection();
        return _kittyPlacements;
    }

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
        _kittyAnchoredPlacements = null;
        _kittyPlaceholderScene = null;
        _kittyPlaceholderRuns = null;
        _kittyProjectionState = null;
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
            Array.Sort(_kittyPlacements, TerminalKittyImagePlacement.ComparePaintOrder);
        }

        InvalidateViewport();
    }

    /// <summary>Clears the current Kitty image snapshot.</summary>
    public void ClearKittyGraphics()
    {
        _kittyAnchoredPlacements = null;
        _kittyPlaceholderScene = null;
        _kittyPlaceholderRuns = null;
        _kittyProjectionState = null;
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
        int maxRows = ViewportRows + (_alternateBufferActive ? 0 : _scrollbackLimit);
        int removedRows = Math.Max(0, _rows.Count + 1 - maxRows);
        TerminalRow row;
        if (removedRows > 0 && _rows.Count > 0)
        {
            // Once a row leaves scrollback, retain its storage for the new bottom
            // row. Clearing all metadata prevents stale wrap/resize state.
            row = _rows[0];
            RemoveRows(0, Math.Min(removedRows, _rows.Count));
            // Reusing the CLR array is not retaining the historical native page.
            // Admission assigns the recycled row to the new tail's capacity.
            row.SnapshotAllocation = null;
            row.SnapshotAllocationRow = 0;
            row.Resize(Columns, DefaultForeground, DefaultBackground);
            row.Clear(DefaultForeground, DefaultBackground);
        }
        else
        {
            row = new TerminalRow(Columns, DefaultForeground, DefaultBackground);
        }

        _rows.Add(row);
        if (maxRows == 0)
        {
            RemoveRows(0, _rows.Count);
        }

        removedRows += RemoveSnapshotQuotaRowsAfterGrowth();

        if (removedRows > 0)
        {
            ShiftRasterGraphicsAfterTopRowsRemoved(removedRows);
            ScrollOffset = _viewportTop;
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

        if (!_snapshotRowGeometry) ResizeActiveRows(Columns);
        EnsureMinimumRows(ViewportRows);
        if (!_snapshotRowGeometry) TrimScrollbackRows();
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

        if (_alternateRows is not null) _snapshotAlternateGeneration = unchecked(_snapshotAlternateGeneration + 1);
        _snapshotPageTracker?.DiscardCursor(1);
        _alternateRows = null;
        _alternateRasterImagesById = null;
        _alternateRasterPlacements = null;
        PruneAnchorsFromRow(0, alternate: true);
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

        RetireSnapshotRows(0, firstViewportRow);
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

        RetireSnapshotRows(0, _rows.Count);
        _rows = rows;
        ClearRasterGraphics();
        ClearKittyGraphics();
        PruneAnchorsFromRow(0, _alternateBufferActive);
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
        _snapshotPageTracker = null;
        _primaryRows = null;
        if (_alternateRows is not null) _snapshotAlternateGeneration = unchecked(_snapshotAlternateGeneration + 1);
        _alternateRows = null;
        _primaryRasterImagesById = null;
        _primaryRasterPlacements = null;
        _alternateRasterImagesById = null;
        _alternateRasterPlacements = null;
        _primaryScrollOffset = 0;
        _viewportTop = 0;
        _trackedAnchors.Clear();
        _anchorRevision++;
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

        RemoveRows(_rows.Count - removableRows, removableRows);
        PruneAnchorsFromRow(_rows.Count, _alternateBufferActive);
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
        _hyperlinkIdentities.Clear();
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
                using GhosttySnapshotPageTracker.RowEdit styles = EditSnapshotRowMetadata(row);
                styles.Clear(row.Columns, row.PreservedColumns - row.Columns);
                row.ClearPreservedCellsFrom(row.Columns, DefaultForeground, DefaultBackground);
            }
        }
    }

    private void EnsureAlternateRows()
    {
        // Restored pages may retain physical widths and incidental history.
        // Switching routes is not a resize or a request to evict those pages.
        if (_snapshotRowGeometry)
        {
            EnsureMinimumRows(ViewportRows);
            ScrollOffset = 0;
            return;
        }
        ResizeActiveRows(Columns);
        EnsureMinimumRows(ViewportRows);
        if (_rows.Count > ViewportRows)
        {
            RemoveRows(ViewportRows, _rows.Count - ViewportRows);
            PruneAnchorsFromRow(ViewportRows, _alternateBufferActive);
        }

        ClampActiveAnchorsToColumns();

        ScrollOffset = 0;
    }

    private void ClearActiveRows()
    {
        PruneAnchorsFromRow(0, _alternateBufferActive);
        for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            ClearRow(_rows[rowIndex]);
        }
    }

    private void ClearViewportRows()
    {
        for (int rowIndex = 0; rowIndex < ViewportRows; rowIndex++)
        {
            ClearRow(GetViewportRow(rowIndex));
        }
    }

    private void AppendBlankRowsAndTrimScrollback(int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }

        if (HasFiniteSnapshotQuota)
        {
            // Each newly exposed tail page has its own byte-recycling event.
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++) AddRow();
            return;
        }

        int maxRows = ViewportRows + (_alternateBufferActive ? 0 : _scrollbackLimit);
        int overflowRows = Math.Max(0, _rows.Count + rowCount - maxRows);
        if (overflowRows > 0)
        {
            RemoveRows(0, overflowRows);
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
        ClampActiveAnchorsToColumns();
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
        ShiftAnchorsAfterTopRowsRemoved(removedRows);
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
            if (rowStart > 0 && row.ReadOnlyCells[rowStart].Width == 0 && !row.ReadOnlyCells[rowStart].IsWideSpacerHead)
            {
                rowStart--;
            }

            if (rowEnd + 1 < row.Columns && row.ReadOnlyCells[rowEnd].Width == 2)
            {
                rowEnd++;
            }

            using GhosttySnapshotPageTracker.RowEdit styles = EditSnapshotRowMetadata(row);
            styles.Clear(rowStart, rowEnd - rowStart + 1);
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
            RemoveRows(0, overflowRows);
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
        List<TerminalScreenAnchor>? reflowCellAnchors = null;
        int cellAnchorOffset = -1;
        int trackedAbsolutePositionCount = trackedAbsolutePositions.Length;
        int liveViewportTopAnchorIndex = -1;
        if (columns != oldColumns &&
            reflowOnResize &&
            (trackedAbsolutePositionCount > 0 || preserveLiveViewportTop || _rasterPlacements.Count > 0 || _trackedAnchors.Count > 0))
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

            cellAnchorOffset = reflowAnchors.Count;
            reflowCellAnchors = AppendTrackedCellReflowAnchors(reflowAnchors);
        }

        int trackedAbsoluteRow = trackedViewportPosition is { } trackedPosition
            ? GetAbsoluteRowForViewportRow(trackedPosition.Row)
            : -1;
        int trackedColumn = trackedViewportPosition is { } position
            ? Math.Clamp(position.Column, 0, oldColumns)
            : 0;
        int mappedAbsoluteRow = trackedAbsoluteRow;
        int mappedColumn = Math.Clamp(trackedColumn, 0, columns);

        // PageList.resize shrinks height before narrower-column REFLOW. The
        // no-reflow path always changes columns first: truncation can make a
        // trailing row blank and eligible for trimming instead of history.
        bool trimBottom = trackedViewportPosition.HasValue &&
            !preserveViewportTopOnRowsIncrease && viewportRows < oldViewportRows;
        bool trimBeforeColumns = reflowOnResize && columns < oldColumns;
        if (trimBottom && trimBeforeColumns)
            TrimUnpinnedBlankRowsForHeightShrink(oldViewportRows - viewportRows, trackedAbsoluteRow);

        if (columns != oldColumns)
        {
            if (reflowOnResize)
            {
                ReflowRows(
                    columns,
                    trackedAbsoluteRow,
                    trackedColumn,
                    reflowAnchors,
                    trimTrailingBlankRows: !preserveViewportTopOnRowsIncrease,
                    out mappedAbsoluteRow,
                    out mappedColumn);
            }
            else
            {
                ResizeRows(columns);
            }
        }

        if (trimBottom && !trimBeforeColumns)
            TrimUnpinnedBlankRowsForHeightShrink(oldViewportRows - viewportRows, mappedAbsoluteRow, reflowAnchors);

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
                if (trackedViewportPosition.HasValue)
                {
                    // Native no-scrollback screens erase the history created
                    // by shrinking: retain the bottom active rows, not the top.
                    removedRows = _rows.Count - ViewportRows;
                    RemoveRows(0, removedRows);
                }
                else
                {
                    RemoveRows(ViewportRows, _rows.Count - ViewportRows);
                }
            }
        }
        else
        {
            int overflowRows = _rows.Count - (ViewportRows + _scrollbackLimit);
            if (overflowRows > 0)
            {
                RemoveRows(0, overflowRows);
                removedRows = overflowRows;
            }
        }

        // Reflow accounts its output before viewport padding is appended.
        // Padding also occupies native page slots, even with unlimited quotas
        // and before the cursor next visits those rows.
        if (TracksSnapshotMetadata && HasNativeSnapshotGeometry)
            _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, SnapshotPageLayout());
        removedRows += RemoveSnapshotQuotaRows(GhosttySnapshotQuotaCheckpoint.Resize);

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

        if (reflowCellAnchors is not null && reflowAnchors is not null)
        {
            RemapTrackedCellAnchors(reflowCellAnchors, reflowAnchors, cellAnchorOffset, removedRows);
        }
        else
        {
            // The raster removal path above also shifts cell anchors. When it
            // was bypassed by a reflow anchor list, apply the removal here.
            if (reflowAnchors is not null) ShiftAnchorsAfterTopRowsRemoved(removedRows);
            PruneAnchorsFromRow(TotalRows, _alternateBufferActive);
            ClampActiveAnchorsToColumns();
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

        return trackedViewportPosition.HasValue && mappedAbsoluteRow < _rows.Count - ViewportRows
            ? new TerminalGridPosition(0, 0)
            : GetViewportPositionForAbsoluteRow(mappedAbsoluteRow, mappedColumn);
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
        if (columns > Columns && PrepareSnapshotResize(columns) is { } tracker)
        {
            GhosttySnapshotColumnResize.Resize(_rows, columns, DefaultForeground, DefaultBackground,
                SnapshotPageLayout(), tracker, this);
            return;
        }
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
        bool trimTrailingBlankRows,
        out int mappedAbsoluteRow,
        out int mappedColumn)
    {
        GhosttySnapshotReflowAllocation? snapshotAllocation = CreateSnapshotReflowAllocation(columns);
        List<GhosttySnapshotReflowAllocation.Source>? snapshotSources = snapshotAllocation is null ? null : [];
        List<TerminalRow> reflowedRows = new(_rows.Count);
        List<TerminalCell> logicalLine = new(Math.Max(Columns, columns));
        List<(int Start, int End, TerminalSemanticPrompt Prompt)>? semanticRows = null;
        ReflowAnchorProcessingIndex[]? trackedPositionIndexes =
            CreateReflowAnchorProcessingOrder(additionalTrackedPositions);
        List<int>? lineTrackedIndexes = trackedPositionIndexes is null ? null : new List<int>();
        List<int>? lineTrackedOffsets = trackedPositionIndexes is null ? null : new List<int>();
        mappedAbsoluteRow = trackedAbsoluteRow;
        mappedColumn = Math.Clamp(trackedColumn, 0, columns);

        int nextTrackedPositionIndex = 0;
        int lastDestinationColumn = 0;
        int rowIndex = 0;
        int sourceRowCount = _rows.Count;
        if (trimTrailingBlankRows)
        {
            int lastPinnedRow = trackedAbsoluteRow;
            if (additionalTrackedPositions is not null)
                foreach (ReflowAnchorPosition pin in additionalTrackedPositions)
                    lastPinnedRow = Math.Max(lastPinnedRow, pin.OldAbsoluteRow);
            // Ghostty defers empty rows and never writes the trailing suffix.
            // Keep pinned rows; the target viewport is padded after reflow.
            while (sourceRowCount > lastPinnedRow + 1 &&
                   GetReflowEndExclusive(_rows[sourceRowCount - 1]) == 0)
                sourceRowCount--;
        }
        while (rowIndex < sourceRowCount)
        {
            logicalLine.Clear();
            snapshotSources?.Clear();
            semanticRows?.Clear();
            lineTrackedIndexes?.Clear();
            lineTrackedOffsets?.Clear();
            int trackedLogicalOffset = -1;

            bool hasContinuation;
            do
            {
                TerminalRow row = _rows[rowIndex];
                int endExclusive = GetReflowEndExclusive(row);
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
                        if (position.CellAnchor && clampedColumn >= endExclusive)
                        {
                            // Ghostty clamps before advancing a deferred newline
                            // or pending wrap. At a full row the physical cursor
                            // remains on the last cell, not column zero.
                            int destinationColumn = logicalLine.Count == 0
                                ? lastDestinationColumn
                                : Math.Min(columns - 1, MapLogicalOffsetToReflowedPosition(
                                    CollectionsMarshal.AsSpan(logicalLine), columns, logicalLine.Count).Column);
                            clampedColumn = Math.Min(clampedColumn, columns - 1 - destinationColumn);
                        }
                        int effectiveTrackedColumn = position.ExtendLineToColumn
                            ? clampedColumn
                            : endExclusive == 0
                                ? clampedColumn
                                : Math.Min(clampedColumn, endExclusive);
                        if (position.ExtendLineToColumn)
                        {
                            endExclusive = Math.Max(endExclusive, clampedColumn + (position.CellAnchor ? 1 : 0));
                        }

                        lineTrackedIndexes!.Add(positionIndex);
                        lineTrackedOffsets!.Add(logicalLine.Count + effectiveTrackedColumn);
                        nextTrackedPositionIndex++;
                    }
                }

                // The live cursor extends the row only after ordinary pins
                // have been clamped, matching PageList.ReflowCursor.reflowRow.
                if (rowIndex == trackedAbsoluteRow)
                {
                    int clampedTrackedColumn = Math.Clamp(trackedColumn, 0, row.Columns);
                    endExclusive = Math.Max(endExclusive, Math.Min(row.Columns, clampedTrackedColumn + 1));
                    trackedLogicalOffset = logicalLine.Count + clampedTrackedColumn;
                }

                if (endExclusive > 0)
                {
                    if (snapshotSources is not null)
                        GhosttySnapshotReflowAllocation.CaptureSource(snapshotSources, logicalLine.Count, row);
                    // Adjacent equal markers form one run. In particular,
                    // ordinary output keeps the original bulk-copy fast path.
                    if (row.SemanticPrompt != TerminalSemanticPrompt.None || semanticRows is { Count: > 0 })
                    {
                        semanticRows ??= new();
                        if (semanticRows.Count == 0 && logicalLine.Count > 0)
                            semanticRows.Add((0, logicalLine.Count, TerminalSemanticPrompt.None));
                        if (semanticRows.Count > 0 && semanticRows[^1].Prompt == row.SemanticPrompt)
                        {
                            var previous = semanticRows[^1];
                            semanticRows[^1] = (previous.Start, logicalLine.Count + endExclusive, previous.Prompt);
                        }
                        else
                        {
                            semanticRows.Add((logicalLine.Count, logicalLine.Count + endExclusive, row.SemanticPrompt));
                        }
                    }
                    ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells[..endExclusive];
                    logicalLine.AddRange(cells);
                }

                hasContinuation = row.WrapsToNext && rowIndex + 1 < sourceRowCount;
                rowIndex++;
            }
            while (hasContinuation);

            int destinationStartRow = reflowedRows.Count;
            TerminalGridPosition? mappedLinePosition = AppendReflowedLogicalLine(
                CollectionsMarshal.AsSpan(logicalLine),
                columns,
                reflowedRows,
                trackedLogicalOffset,
                semanticRows is null ? default : CollectionsMarshal.AsSpan(semanticRows),
                snapshotAllocation,
                snapshotSources is null ? default : CollectionsMarshal.AsSpan(snapshotSources),
                ref lastDestinationColumn);

            if (mappedLinePosition is { } mappedPosition)
            {
                if (trackedColumn < Columns)
                    mappedPosition = MapLogicalCellOffsetToReflowedPosition(
                        CollectionsMarshal.AsSpan(logicalLine), columns, trackedLogicalOffset);
                mappedAbsoluteRow = destinationStartRow + mappedPosition.Row;
                mappedColumn = mappedPosition.Column;
            }

            if (additionalTrackedPositions is not null &&
                lineTrackedIndexes is not null &&
                lineTrackedOffsets is not null)
            {
                for (int i = 0; i < lineTrackedIndexes.Count; i++)
                {
                    int positionIndex = lineTrackedIndexes[i];
                    ReflowAnchorPosition position = additionalTrackedPositions[positionIndex];
                    TerminalGridPosition mappedAnchorPosition = position.CellAnchor
                        ? MapLogicalCellOffsetToReflowedPosition(CollectionsMarshal.AsSpan(logicalLine), columns, lineTrackedOffsets[i])
                        : MapLogicalOffsetToReflowedPosition(CollectionsMarshal.AsSpan(logicalLine), columns, lineTrackedOffsets[i]);
                    int maxColumn = position.AllowEndColumn ? columns : Math.Max(0, columns - 1);
                    position.NewAbsoluteRow = destinationStartRow + mappedAnchorPosition.Row;
                    position.NewColumn = Math.Clamp(mappedAnchorPosition.Column, 0, maxColumn);
                    position.IsMapped = true;
                    additionalTrackedPositions[positionIndex] = position;
                }
            }
        }

        snapshotAllocation?.Finish(reflowedRows, DefaultForeground, DefaultBackground);
        _rows.Clear();
        _rows.AddRange(reflowedRows);
        snapshotAllocation?.AccountResult(this, _rows);
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
                // A trailing spacer belongs to the final wide cell. Its pen
                // need not equal the head's (a late selector can change it).
                return Math.Min(cells.Length, i + Math.Max(1, (int)cells[i].Width));
            }
        }

        return row.SemanticPrompt != TerminalSemanticPrompt.None && row.Columns > 0 ? 1 : 0;
    }

    private static bool IsReflowLineEndCell(ref readonly TerminalCell cell)
    {
        // Ghostty Cell.isEmpty retains printed spaces, wide spacers and
        // background-only cells. A literal space is not an unwritten cell.
        return cell.Width != 1 || cell.HasContent ||
            cell.BackgroundIdentity.Kind != TerminalColorKind.Default;
    }

    private TerminalGridPosition? AppendReflowedLogicalLine(
        ReadOnlySpan<TerminalCell> logicalLine,
        int columns,
        List<TerminalRow> destination,
        int trackedLogicalOffset,
        ReadOnlySpan<(int Start, int End, TerminalSemanticPrompt Prompt)> semanticRows,
        GhosttySnapshotReflowAllocation? snapshotAllocation,
        ReadOnlySpan<GhosttySnapshotReflowAllocation.Source> snapshotSources,
        ref int lastDestinationColumn)
    {
        int destinationStart = destination.Count;
        TerminalGridPosition? mappedPosition = null;

        if (logicalLine.IsEmpty)
        {
            TerminalRow blank = new(columns, DefaultForeground, DefaultBackground);
            destination.Add(blank);
            snapshotAllocation?.DeferBlank(blank);
            return trackedLogicalOffset >= 0
                ? new TerminalGridPosition(Math.Clamp(trackedLogicalOffset, 0, columns), 0)
                : null;
        }

        int sourceIndex = 0;
        int semanticIndex = 0;
        int snapshotSourceIndex = 0;
        while (sourceIndex < logicalLine.Length)
        {
            TerminalRow row = TerminalRow.CreateForReflow(columns);
            if (snapshotAllocation is not null)
            {
                while (snapshotSourceIndex + 1 < snapshotSources.Length && sourceIndex >= snapshotSources[snapshotSourceIndex + 1].Start)
                    snapshotSourceIndex++;
                snapshotAllocation.Append(row, snapshotSources[snapshotSourceIndex].Page);
            }
            row.SemanticPrompt = semanticRows.IsEmpty ? TerminalSemanticPrompt.None : semanticRows[semanticIndex].Prompt;
            int destinationRow = destination.Count - destinationStart;
            row.IsWrapContinuation = destinationRow > 0;
            int column = 0;
            bool wideWrap = false;

            while (sourceIndex < logicalLine.Length && column < columns)
            {
                while (semanticIndex + 1 < semanticRows.Length && sourceIndex >= semanticRows[semanticIndex + 1].Start)
                    row.SemanticPrompt = semanticRows[++semanticIndex].Prompt;
                if (mappedPosition is null && trackedLogicalOffset >= 0 && trackedLogicalOffset <= sourceIndex)
                {
                    mappedPosition = new TerminalGridPosition(column, destinationRow);
                }

                // Cell payloads still copy in bulk. Snapshot-aware reflow also
                // records native metadata allocation/copies before the payload;
                // ordinary screens skip that bookkeeping entirely.
                int runLength = GetReflowCopyRunLength(
                    logicalLine[sourceIndex..], semanticRows.IsEmpty ? columns - column
                        : Math.Min(columns - column, semanticRows[semanticIndex].End - sourceIndex));
                if (runLength > 0)
                {
                    snapshotAllocation?.CopyMetadata(row, column, snapshotSources, ref snapshotSourceIndex, sourceIndex, runLength);
                    logicalLine.Slice(sourceIndex, runLength).CopyTo(row.Cells[column..]);
                    if (mappedPosition is null && trackedLogicalOffset >= 0 &&
                        trackedLogicalOffset <= sourceIndex + runLength)
                    {
                        int offset = trackedLogicalOffset - sourceIndex;
                        if (offset > 0 && offset < runLength &&
                            logicalLine[sourceIndex + offset - 1].Width == 2)
                        {
                            offset++;
                        }

                        mappedPosition = new TerminalGridPosition(column + offset, destinationRow);
                    }

                    sourceIndex += runLength;
                    column += runLength;
                    continue;
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
                    row[column] = TerminalCell.Empty(DefaultForeground, DefaultBackground);
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
                    wideWrap = true;
                    break;
                }

                cell.Width = (byte)width;
                snapshotAllocation?.CopyMetadata(row, column, snapshotSources, ref snapshotSourceIndex, sourceIndex, 1);
                row[column] = cell;

                if (width == 2 && column + 1 < columns)
                {
                    snapshotAllocation?.CopyMetadata(row, column + 1, snapshotSources, ref snapshotSourceIndex,
                        sourceStep == 2 && IsNormalizedWideSpacer(in logicalLine[sourceIndex + 1]) ? sourceIndex + 1 : sourceIndex, 1,
                        includeGraphemes: sourceStep == 2 && IsNormalizedWideSpacer(in logicalLine[sourceIndex + 1]));
                    row[column + 1] = sourceStep == 2 && IsNormalizedWideSpacer(in logicalLine[sourceIndex + 1])
                        ? logicalLine[sourceIndex + 1] : CreateWideSpacer(cell);
                }

                sourceIndex += sourceStep;
                column += width;

                if (mappedPosition is null && trackedLogicalOffset >= 0 && trackedLogicalOffset <= sourceIndex)
                {
                    mappedPosition = new TerminalGridPosition(column, destinationRow);
                }
            }

            // Native reflow copies the next source row's metadata before
            // consuming a pending wrap. Preserve that overwrite at exact row
            // boundaries, without rescanning the logical line for each marker.
            while (semanticIndex + 1 < semanticRows.Length && sourceIndex >= semanticRows[semanticIndex + 1].Start)
                row.SemanticPrompt = semanticRows[++semanticIndex].Prompt;
            row.Cells[column..].Fill(TerminalCell.Empty(DefaultForeground, DefaultBackground));
            if (wideWrap)
            {
                row[column].Width = 0;
                row[column].IsWideSpacerHead = true;
            }
            row.WrapsToNext = sourceIndex < logicalLine.Length;
            destination.Add(row);
            lastDestinationColumn = Math.Min(column, columns - 1);
        }

        if (mappedPosition is null && trackedLogicalOffset >= 0)
        {
            int lastRow = Math.Max(0, destination.Count - destinationStart - 1);
            mappedPosition = new TerminalGridPosition(0, lastRow);
        }

        return mappedPosition;
    }

    private static TerminalGridPosition MapLogicalOffsetToReflowedPosition(
        ReadOnlySpan<TerminalCell> logicalLine,
        int columns,
        int trackedLogicalOffset)
    {
        if (logicalLine.IsEmpty)
        {
            return new TerminalGridPosition(Math.Clamp(trackedLogicalOffset, 0, columns), 0);
        }

        int sourceIndex = 0;
        int destinationRow = 0;
        int column = 0;
        while (sourceIndex < logicalLine.Length)
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

    private static int GetReflowSourceStep(ReadOnlySpan<TerminalCell> logicalLine, int sourceIndex)
    {
        TerminalCell cell = logicalLine[sourceIndex];
        if (cell.Width <= 1)
        {
            return 1;
        }

        return sourceIndex + 1 < logicalLine.Length && logicalLine[sourceIndex + 1].Width == 0 &&
            !logicalLine[sourceIndex + 1].IsWideSpacerHead
            ? 2
            : 1;
    }

    private static int GetReflowCopyRunLength(ReadOnlySpan<TerminalCell> cells, int availableColumns)
    {
        int limit = Math.Min(cells.Length, availableColumns);
        int length = 0;
        while (length < limit)
        {
            ref readonly TerminalCell cell = ref cells[length];
            if (cell.Width == 1)
            {
                length++;
                continue;
            }

            if (cell.Width != 2 || length + 1 >= limit ||
                !IsNormalizedWideSpacer(in cells[length + 1]))
            {
                break;
            }

            length += 2;
        }

        return length;
    }

    // A late variation selector can print the tail with a different current pen
    // (including OSC 133 classification). Such pairs are valid, not malformed.
    private static bool IsNormalizedWideSpacer(in TerminalCell tail)
        => tail.Width == 0 && !tail.IsWideSpacerHead && tail.Codepoint == 0 && tail.Grapheme is null;

    private static TerminalCell CreateWideSpacer(TerminalCell source) => new()
    {
        Codepoint = 0,
        Grapheme = null,
        Foreground = source.Foreground,
        Background = source.Background,
        ForegroundIdentity = source.ForegroundIdentity,
        BackgroundIdentity = source.BackgroundIdentity,
        UnderlineIdentity = source.UnderlineIdentity,
        Attributes = source.Attributes,
        UnderlineStyle = source.UnderlineStyle,
        UnderlineColor = source.UnderlineColor,
        HasUnderlineColor = source.HasUnderlineColor,
        Decorations = source.Decorations,
        HasBackground = source.HasBackground,
        HyperlinkId = source.HyperlinkId,
        IsProtected = source.IsProtected,
        SemanticContent = source.SemanticContent,
        Width = 0,
    };

    private struct ReflowAnchorPosition
    {
        public ReflowAnchorPosition(
            int oldAbsoluteRow,
            int oldColumn,
            bool allowEndColumn = false,
            bool extendLineToColumn = true,
            bool cellAnchor = false)
        {
            OldAbsoluteRow = oldAbsoluteRow;
            OldColumn = oldColumn;
            AllowEndColumn = allowEndColumn;
            ExtendLineToColumn = extendLineToColumn;
            CellAnchor = cellAnchor;
            NewAbsoluteRow = oldAbsoluteRow;
            NewColumn = oldColumn;
            IsMapped = false;
        }

        public int OldAbsoluteRow { get; }

        public int OldColumn { get; }

        public bool AllowEndColumn { get; }

        public bool ExtendLineToColumn { get; }

        public bool CellAnchor { get; }

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
