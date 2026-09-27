// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Carries logical native page ownership through the managed reflow traversal.
/// Cell movement and anchor mapping remain the screen's responsibility; refused
/// metadata is removed from its unpublished destination payload. This object
/// exists only for one resize and never retains source cell storage.
/// </summary>
internal sealed class GhosttySnapshotReflowAllocation
{
    internal readonly record struct Source(int Start, GhosttySnapshotPageAllocation Page, int Row);

    private readonly GhosttySnapshotAllocation _layout;
    private readonly int _columns;
    private readonly TerminalTheme _theme;
    private readonly Dictionary<GhosttySnapshotPageAllocation, int> _sourceRows = [];
    private GhosttySnapshotPageAllocation _destinationPage;
    private int _nextRow;
    private List<TerminalRow>? _deferredBlanks;
    private GhosttySnapshotPageAllocation? _memoSource;
    private GhosttySnapshotPageCapacity _memoCapacity;
    private readonly GhosttySnapshotPageTracker? _tracker;
    private readonly Dictionary<GhosttySnapshotPageAllocation, GhosttySnapshotPageStorage>? _sourceStorage;
    private readonly List<TerminalRow> _destinationRows = [];
    private GhosttySnapshotPageStorage? _destinationStorage;
    private GhosttySnapshotStyleStorage.CopyCache _styleCache;

    internal GhosttySnapshotReflowAllocation(TerminalRowBuffer source, int columns, GhosttySnapshotAllocation layout,
        GhosttySnapshotPageTracker? tracker = null, TerminalScreen? screen = null)
    {
        _layout = layout;
        _columns = columns;
        _theme = screen?.Theme ?? TerminalTheme.Dark;
        _tracker = tracker;
        // Reconciliation can replace source capacities. Capture provenance only
        // afterwards; the borrowed tables then stay read-only for this resize.
        _sourceStorage = tracker?.ReflowSources(source, layout, screen);
        for (int i = 0; i < source.Count; i++)
        {
            GhosttySnapshotPageAllocation page = source[i].SnapshotAllocation
                ?? throw new InvalidOperationException("Reflow requires accounted source pages.");
            _sourceRows.TryGetValue(page, out int count);
            _sourceRows[page] = count + 1;
        }

        // PageList.resizeCols always creates its first destination from the
        // first source page, including when all source rows are blank.
        GhosttySnapshotPageAllocation first = source[0].SnapshotAllocation!;
        _destinationPage = new(Adjust(first, firstPage: true));
        if (tracker is not null) _destinationStorage = new(_destinationPage.Capacity);
    }

    internal static void CaptureSource(List<Source> sources, int offset, TerminalRow row)
    {
        GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
        // Keep physical row offsets even within one page: trimmed hard lines,
        // rotations and skipped spacer heads need not be contiguous cells.
        sources.Add(new(offset, page, row.SnapshotAllocationRow));
    }

    internal void DeferBlank(TerminalRow row) => (_deferredBlanks ??= []).Add(row);

    internal void Append(TerminalRow row, GhosttySnapshotPageAllocation source)
    {
        FlushBlanks(source);
        AppendCore(row, source);
    }

    internal void Finish(List<TerminalRow> destination, uint foreground, uint background)
    {
        // Normal reflow trims unpinned trailing blanks. The host's explicit
        // preserve-viewport policy can retain them; any extra pages then use
        // the same standard allocation as post-reflow viewport padding.
        FlushBlanks(null);
        if (destination.Count == 0)
        {
            TerminalRow row = new(_columns, foreground, background);
            AppendCore(row, null);
            destination.Add(row);
        }
        FinishPage();
    }

    internal void AccountResult(TerminalScreen screen, TerminalRowBuffer rows)
        => GhosttySnapshotLiveAllocation.Measure(screen, rows, _layout);

    private void FlushBlanks(GhosttySnapshotPageAllocation? source)
    {
        if (_deferredBlanks is null) return;
        foreach (TerminalRow row in _deferredBlanks) AppendCore(row, source);
        _deferredBlanks.Clear();
    }

    private void AppendCore(TerminalRow row, GhosttySnapshotPageAllocation? source)
    {
        if (_nextRow == _destinationPage.Capacity.Rows)
        {
            FinishPage();
            GhosttySnapshotPageCapacity capacity = source is null ? _layout.InitialCapacity(_columns) : NextCapacity(source);
            _destinationPage = new(capacity);
            if (_tracker is not null) _destinationStorage = new(capacity);
            _destinationRows.Clear();
            _nextRow = 0;
        }
        row.SnapshotAllocation = _destinationPage;
        row.SnapshotAllocationRow = _nextRow++;
        row.SnapshotAllocationUnmodified = false;
        _destinationRows.Add(row);
    }

    // The caller must copy the payload first: a representable capacity refusal
    // removes only the unaccepted fields, without poisoning the whole page.
    internal void CopyMetadata(TerminalRow row, int column, ReadOnlySpan<Source> sources, ref int sourceRun, int sourceIndex, int count,
        bool includeGraphemes = true)
    {
        if (_destinationStorage is null || count == 0 || _destinationPage.MetadataOverflow) return;
        while (count > 0)
        {
            while (sourceRun + 1 < sources.Length && sourceIndex >= sources[sourceRun + 1].Start) sourceRun++;
            Source source = sources[sourceRun];
            int run = sourceRun + 1 < sources.Length ? Math.Min(count, sources[sourceRun + 1].Start - sourceIndex) : count;
            if (!_sourceStorage!.TryGetValue(source.Page, out GhosttySnapshotPageStorage? storage))
            {
                MarkOverflow();
                return;
            }
            int offset = checked(source.Row * source.Page.Capacity.Columns + sourceIndex - source.Start);
            int target = checked(row.SnapshotAllocationRow * _columns + column);
            while (run > 0)
            {
                // Native copies grapheme, hyperlink, then style. Keep grouped
                // style-only copies when there is no other per-cell metadata.
                int batch = storage.Graphemes.Count == 0 && storage.Hyperlinks.CellCount == 0 ? run : 1;
                if (batch == 1 && !CopyCellExtras(row, column, source.Page, storage, offset, ref target, includeGraphemes))
                {
                    offset++; target++; column++; sourceIndex++; count--; run--;
                    continue;
                }
                bool retried = false;
                while (batch > 0)
                {
                    GhosttySnapshotSetAddResult result = _destinationStorage!.Styles.CopyCellsFrom(target, storage.Styles, offset, batch, ref _styleCache, out int copied);
                    offset += copied; target += copied; column += copied; sourceIndex += copied; count -= copied; run -= copied; batch -= copied;
                    if (result == GhosttySnapshotSetAddResult.Success) break;
                    if (copied > 0) retried = false;
                    if (!retried && GrowOrMove(result == GhosttySnapshotSetAddResult.OutOfMemory ? GhosttySnapshotCapacityDimension.Styles : null,
                        row, column, source.Page, ref target, out bool moved))
                    {
                        retried = !moved;
                        continue;
                    }
                    // Unsafe native builds drop only this cell's styling on an
                    // unexpected second refusal; first-row OutOfSpace does the
                    // same, retaining any accepted grapheme and hyperlink.
                    DropMetadata(row, column, grapheme: false, hyperlink: false, style: true);
                    offset++; target++; column++; sourceIndex++; count--; run--; batch--;
                    retried = false;
                }
            }
        }
    }

    // The screen has already copied the payload into its unpublished row.
    // Refusals filter it in place so a later bulk copy cannot resurrect data
    // that was never accepted by the destination metadata allocators.
    private bool CopyCellExtras(TerminalRow row, int column, GhosttySnapshotPageAllocation source,
        GhosttySnapshotPageStorage storage, int offset, ref int target, bool includeGraphemes)
    {
        if (includeGraphemes && storage.Graphemes.SuffixLength(offset) != 0)
        {
            while (_destinationStorage!.Graphemes.CopyReflowCellFrom(target, storage.Graphemes, offset) != GhosttySnapshotGraphemeAddResult.Success)
            {
                if (GrowOrMove(GhosttySnapshotCapacityDimension.GraphemeBytes, row, column, source, ref target, out _)) continue;
                DropMetadata(row, column, grapheme: true, hyperlink: true, style: true);
                return false;
            }
        }
        bool retriedSet = false;
        while (true)
        {
            GhosttySnapshotHyperlinkAddResult result = _destinationStorage!.Hyperlinks.CopyReflowCellFrom(target, storage.Hyperlinks, offset);
            if (result == GhosttySnapshotHyperlinkAddResult.Success) return true;
            bool setFailure = result is GhosttySnapshotHyperlinkAddResult.SetFull or GhosttySnapshotHyperlinkAddResult.SetNeedsRehash;
            if (setFailure && retriedSet)
            {
                DropMetadata(row, column, grapheme: false, hyperlink: true, style: false);
                return true; // Native continues with the independent style.
            }
            if (!GrowOrMove(GhosttySnapshotHyperlinkStorage.GrowthDimension(result), row, column, source, ref target, out bool moved))
            {
                DropMetadata(row, column, grapheme: false, hyperlink: true, style: true);
                return false;
            }
            // String/map growth does not consume the one set retry. A split
            // starts a fresh row attempt; accepted graphemes stay owned once.
            retriedSet = !moved && (retriedSet || setFailure);
        }
    }

    private bool GrowOrMove(GhosttySnapshotCapacityDimension? dimension, TerminalRow row, int column,
        GhosttySnapshotPageAllocation source, ref int target, out bool moved)
    {
        moved = false;
        if (GrowMetadata(dimension)) return true;
        if (row.SnapshotAllocationRow == 0) return false;
        MoveLastRow(row, source);
        target = column;
        moved = true;
        return true;
    }

    private void MoveLastRow(TerminalRow row, GhosttySnapshotPageAllocation source)
    {
        if (!ReferenceEquals(_destinationRows[^1], row) || row.SnapshotAllocationRow != _nextRow - 1)
            throw new InvalidOperationException("Reflow can only split its current final row.");
        GhosttySnapshotPageCapacity capacity = NextCapacity(source);
        GhosttySnapshotPageStorage previous = _destinationStorage!;
        GhosttySnapshotPageStorage replacement = new(capacity);
        int start = checked(row.SnapshotAllocationRow * _columns);
        GhosttySnapshotStyleStorage.CopyCache cache = default;
        for (int column = 0; column < _columns;)
        {
            int batch = previous.Graphemes.Count == 0 && previous.Hyperlinks.CellCount == 0 ? _columns - column : 1;
            if (batch == 1 &&
                (replacement.Graphemes.CopyCellFrom(column, previous.Graphemes, start + column) != GhosttySnapshotGraphemeAddResult.Success ||
                 replacement.Hyperlinks.CopyCellFrom(column, previous.Hyperlinks, start + column) != GhosttySnapshotHyperlinkAddResult.Success))
                throw new InvalidOperationException("Snapshot reflow row metadata clone failed.");
            if (replacement.Styles.CopyCellsFrom(column, previous.Styles, start + column, batch, ref cache, out int copied) != GhosttySnapshotSetAddResult.Success)
                throw new InvalidOperationException("Snapshot reflow row metadata clone failed.");
            column += copied;
        }
        GhosttySnapshotPageAllocation page = new(capacity);
        // Commit only after the complete row clone is available. Resetting the
        // old final row keeps its page's dead-ID/string history, just as resetRow.
        previous.Graphemes.ClearCells(start, _columns);
        previous.Hyperlinks.ClearCells(start, _columns);
        previous.Styles.ClearCells(start, _columns);
        _destinationRows.RemoveAt(_destinationRows.Count - 1);
        FinishPage();
        _destinationRows.Clear();
        _destinationRows.Add(row);
        _destinationPage = page;
        _destinationStorage = replacement;
        _nextRow = 1;
        row.SnapshotAllocation = page;
        row.SnapshotAllocationRow = 0;
        row.SnapshotAllocationUnmodified = false;
        // Resume at the failed metadata stage. Re-inserting an already cloned
        // grapheme would violate putNoClobber; accepted owners must not leak or
        // acquire duplicate references when replaying the partially written cell.
    }

    private void DropMetadata(TerminalRow row, int column, bool grapheme, bool hyperlink, bool style)
    {
        TerminalCell original = row.ReadOnlyCells[column];
        TerminalCell cell = style ? GhosttySnapshotLivePage.DecodeStyle(default, _theme) : original;
        if (style)
        {
            cell.Codepoint = original.Codepoint;
            cell.Width = original.Width;
            cell.IsProtected = original.IsProtected;
            cell.SemanticContent = original.SemanticContent;
            cell.IsWideSpacerHead = original.IsWideSpacerHead;
        }
        cell.Grapheme = grapheme ? null : original.Grapheme;
        cell.HyperlinkId = hyperlink ? 0 : original.HyperlinkId;
        row[column] = cell;
    }

    private bool GrowMetadata(GhosttySnapshotCapacityDimension? dimension)
    {
        GhosttySnapshotPageCapacity capacity = _destinationPage.Capacity;
        ulong used = _destinationStorage!.Usage(dimension);
        if (dimension is { } growth && !_layout.TryIncreaseCapacity(capacity, growth, used, _nextRow, out capacity))
            return false;
        if (!_destinationStorage!.Rebuild(capacity, restoreCursor: false, out GhosttySnapshotPageStorage? rebuilt))
            throw new InvalidOperationException("Snapshot reflow page metadata clone failed.");
        // Like ReflowCursor.increaseCapacity, copy only the populated prefix;
        // there is no cursor pen on the replacement until Screen.resize ends.
        _destinationStorage = rebuilt;
        ReplaceDestination(new(capacity));
        return true;
    }

    private void MarkOverflow() => ReplaceDestination(new(_destinationPage.Capacity, metadataOverflow: true));

    private void ReplaceDestination(GhosttySnapshotPageAllocation page)
    {
        _destinationPage = page;
        foreach (TerminalRow row in _destinationRows) row.SnapshotAllocation = page;
    }

    private void FinishPage()
    {
        if (_destinationStorage is not null) _tracker!.InstallReflowPage(_destinationPage, _destinationStorage, _destinationRows);
    }

    private GhosttySnapshotPageCapacity NextCapacity(GhosttySnapshotPageAllocation source)
    {
        if (ReferenceEquals(source, _memoSource)) return _memoCapacity;
        _memoSource = source;
        return _memoCapacity = Adjust(source, firstPage: false);
    }

    private GhosttySnapshotPageCapacity Adjust(GhosttySnapshotPageAllocation source, bool firstPage)
    {
        if (_layout.TryAdjustColumns(source.Capacity, _columns, out GhosttySnapshotPageCapacity adjusted)) return adjusted;
        // Match resizeCols' first-page fallback and ReflowCursor's later-page
        // fallback independently. Only the latter caps inherited rows at 215.
        return source.Capacity with
        {
            Columns = (ushort)_columns,
            Rows = (ushort)Math.Min(_sourceRows[source], firstPage ? source.Capacity.Rows : 215),
        };
    }
}
