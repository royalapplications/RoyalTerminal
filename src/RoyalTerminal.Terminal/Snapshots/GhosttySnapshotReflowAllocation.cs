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
    private readonly GhosttySnapshotStyleTracker? _tracker;
    private readonly Dictionary<GhosttySnapshotPageAllocation, GhosttySnapshotStyleStorage>? _sourceStyles;
    private readonly List<TerminalRow> _destinationRows = [];
    private GhosttySnapshotStyleStorage? _destinationStyles;
    private GhosttySnapshotStyleStorage.CopyCache _styleCache;

    internal GhosttySnapshotReflowAllocation(TerminalRowBuffer source, int columns, GhosttySnapshotAllocation layout,
        GhosttySnapshotStyleTracker? tracker = null)
    {
        _layout = layout;
        _columns = columns;
        _tracker = tracker;
        // Reconciliation can replace source capacities. Capture provenance only
        // afterwards; the borrowed tables then stay read-only for this resize.
        _sourceStyles = tracker?.ReflowSources(source, layout);
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
        if (tracker is not null) _destinationStyles = new(_destinationPage.Capacity.Styles);
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
            if (_tracker is not null) _destinationStyles = new(capacity.Styles);
            _destinationRows.Clear();
            _nextRow = 0;
        }
        row.SnapshotAllocation = _destinationPage;
        row.SnapshotAllocationRow = _nextRow++;
        row.SnapshotAllocationUnmodified = false;
        _destinationRows.Add(row);
    }

    internal void CopyStyles(TerminalRow row, int column, ReadOnlySpan<Source> sources, ref int sourceRun, int sourceIndex, int count)
    {
        if (_destinationStyles is null || count == 0 || _destinationPage.MetadataOverflow) return;
        while (count > 0)
        {
            while (sourceRun + 1 < sources.Length && sourceIndex >= sources[sourceRun + 1].Start) sourceRun++;
            Source source = sources[sourceRun];
            int run = sourceRun + 1 < sources.Length ? Math.Min(count, sources[sourceRun + 1].Start - sourceIndex) : count;
            if (!_sourceStyles!.TryGetValue(source.Page, out GhosttySnapshotStyleStorage? styles))
            {
                MarkOverflow();
                return;
            }
            int offset = checked(source.Row * source.Page.Capacity.Columns + sourceIndex - source.Start);
            int target = checked(row.SnapshotAllocationRow * _columns + column);
            bool retried = false;
            while (run > 0)
            {
                GhosttySnapshotSetAddResult result = _destinationStyles!.CopyCellsFrom(target, styles, offset, run, ref _styleCache, out int copied);
                offset += copied; target += copied; column += copied; sourceIndex += copied; count -= copied; run -= copied;
                if (result == GhosttySnapshotSetAddResult.Success) break;
                if (copied > 0) retried = false;
                if (retried) { MarkOverflow(); return; }
                if (!GrowStyles(result)) return;
                retried = true;
            }
        }
    }

    private bool GrowStyles(GhosttySnapshotSetAddResult reason)
    {
        GhosttySnapshotPageCapacity capacity = _destinationPage.Capacity;
        if (reason == GhosttySnapshotSetAddResult.OutOfMemory &&
            !_layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles,
                (ulong)_destinationStyles!.Count, _nextRow, out capacity))
        {
            MarkOverflow();
            return false;
        }
        if (_destinationStyles!.Rebuild(capacity.Styles, out GhosttySnapshotStyleStorage? rebuilt) != GhosttySnapshotSetAddResult.Success)
        {
            MarkOverflow();
            return false;
        }
        // Like ReflowCursor.increaseCapacity, copy only the populated prefix;
        // there is no cursor pen on the replacement until Screen.resize ends.
        _destinationStyles = rebuilt;
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
        if (_destinationStyles is not null) _tracker!.InstallReflowPage(_destinationPage, _destinationStyles, _destinationRows);
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
