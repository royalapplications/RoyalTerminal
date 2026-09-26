// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Carries logical native page ownership through the managed reflow traversal.
/// Cell movement and anchor mapping remain the screen's responsibility. This
/// object exists only for one resize and never retains source cell storage.
/// </summary>
internal sealed class GhosttySnapshotReflowAllocation
{
    internal readonly record struct Source(int Start, GhosttySnapshotPageAllocation Page, int Row);

    private readonly GhosttySnapshotAllocation _layout;
    private readonly int _columns;
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
        GhosttySnapshotPageTracker? tracker = null)
    {
        _layout = layout;
        _columns = columns;
        _tracker = tracker;
        // Reconciliation can replace source capacities. Capture provenance only
        // afterwards; the borrowed tables then stay read-only for this resize.
        _sourceStorage = tracker?.ReflowSources(source, layout);
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
            GhosttySnapshotPageCapacity capacity;
            if (source is null)
                capacity = _layout.InitialCapacity(_columns);
            else if (ReferenceEquals(source, _memoSource))
                capacity = _memoCapacity;
            else
            {
                capacity = Adjust(source, firstPage: false);
                _memoSource = source;
                _memoCapacity = capacity;
            }
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
            bool retried = false;
            while (run > 0)
            {
                // Native copies each cell's grapheme before its style. Keep the
                // grouped style-only fast path when the source has no graphemes.
                int batch = storage.Graphemes.Count == 0 ? run : 1;
                if (includeGraphemes && !retried && batch == 1 && storage.Graphemes.SuffixLength(offset) != 0 &&
                    _destinationStorage!.Graphemes.CopyCellFrom(target, storage.Graphemes, offset) != GhosttySnapshotGraphemeAddResult.Success)
                {
                    if (!GrowMetadata(GhosttySnapshotCapacityDimension.GraphemeBytes)) return;
                    if (_destinationStorage!.Graphemes.CopyCellFrom(target, storage.Graphemes, offset) != GhosttySnapshotGraphemeAddResult.Success)
                    { MarkOverflow(); return; }
                }
                GhosttySnapshotSetAddResult result = _destinationStorage!.Styles.CopyCellsFrom(target, storage.Styles, offset, batch, ref _styleCache, out int copied);
                offset += copied; target += copied; column += copied; sourceIndex += copied; count -= copied; run -= copied;
                if (result == GhosttySnapshotSetAddResult.Success) { retried = false; continue; }
                if (copied > 0) retried = false;
                if (retried) { MarkOverflow(); return; }
                if (!GrowMetadata(result == GhosttySnapshotSetAddResult.OutOfMemory ? GhosttySnapshotCapacityDimension.Styles : null)) return;
                retried = true;
            }
        }
    }

    private bool GrowMetadata(GhosttySnapshotCapacityDimension? dimension)
    {
        GhosttySnapshotPageCapacity capacity = _destinationPage.Capacity;
        ulong used = dimension == GhosttySnapshotCapacityDimension.GraphemeBytes
            ? _destinationStorage!.Graphemes.AllocatedBytes : (ulong)_destinationStorage!.Styles.Count;
        if (dimension is { } growth && !_layout.TryIncreaseCapacity(capacity, growth, used, _nextRow, out capacity))
        {
            MarkOverflow();
            return false;
        }
        if (!_destinationStorage!.Rebuild(capacity, restoreCursor: false, out GhosttySnapshotPageStorage? rebuilt))
        {
            MarkOverflow();
            return false;
        }
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
