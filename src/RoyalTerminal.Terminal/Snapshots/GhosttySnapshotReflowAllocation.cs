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
    internal readonly record struct Source(int Start, GhosttySnapshotPageAllocation Page);

    private readonly GhosttySnapshotAllocation _layout;
    private readonly int _columns;
    private readonly Dictionary<GhosttySnapshotPageAllocation, int> _sourceRows = [];
    private GhosttySnapshotPageAllocation _destinationPage;
    private int _nextRow;
    private List<TerminalRow>? _deferredBlanks;
    private GhosttySnapshotPageAllocation? _memoSource;
    private GhosttySnapshotPageCapacity _memoCapacity;

    internal GhosttySnapshotReflowAllocation(TerminalRowBuffer source, int columns, GhosttySnapshotAllocation layout)
    {
        _layout = layout;
        _columns = columns;
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
    }

    internal static void CaptureSource(List<Source> sources, int offset, TerminalRow row)
    {
        GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
        if (sources.Count == 0 || !ReferenceEquals(sources[^1].Page, page))
            sources.Add(new(offset, page));
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
            _nextRow = 0;
        }
        row.SnapshotAllocation = _destinationPage;
        row.SnapshotAllocationRow = _nextRow++;
        row.SnapshotAllocationUnmodified = false;
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
