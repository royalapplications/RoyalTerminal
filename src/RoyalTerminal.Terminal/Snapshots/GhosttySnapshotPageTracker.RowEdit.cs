// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    internal RowEdit EditRow(TerminalRowBuffer rows, TerminalRow row, GhosttySnapshotAllocation layout)
    {
        if (row.SnapshotAllocation is not { MetadataOverflow: false } page) return default;
        State state;
        if (_pages.TryGetValue(page, out State? known) &&
            known.Revisions.TryGetValue(row.SnapshotAllocationRow, out ulong? revision) && revision == row.SnapshotMetadataRevision)
            state = Exclusive(page, known);
        else
        {
            List<TerminalRow> group = Group(rows, page);
            state = Writable(page, group);
            if (!Synchronize(ref page, state, group, layout)) return default;
        }
        return new(this, rows, row, state, layout);
    }

    // Stack-only by usage: no per-edit closure, journal or dense cell snapshot.
    // Callers edit allocator references before the corresponding cells and must
    // cover every metadata-changing cell in the scope. Untracked screens use default.
    internal readonly struct RowEdit(GhosttySnapshotPageTracker? owner, TerminalRowBuffer rows,
        TerminalRow row, State state, GhosttySnapshotAllocation layout) : IDisposable
    {
        private GhosttySnapshotPageTracker? Owner => owner;
        private TerminalRow Row => row;
        private State AllocatorState => state;
        private int Offset => checked(row.SnapshotAllocationRow * row.SnapshotAllocation!.Capacity.Columns);

        internal void Clear(int start, int count)
        {
            if (owner is null || count <= 0) return;
            state.Storage.Graphemes.ClearCells(checked(Offset + start), count);
            state.Storage.Styles.ClearCells(checked(Offset + start), count);
        }

        internal void Write(int column, GhosttySnapshotStyle style)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            state.Storage.Graphemes.Clear(checked(Offset + column));
            GhosttySnapshotSetAddResult result = state.Storage.Styles.ChangeCell(checked(Offset + column), style);
            if (result == GhosttySnapshotSetAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (owner.Grow(ref page, state, group, result, layout) &&
                state.Storage.Styles.ChangeCell(checked(Offset + column), style) != GhosttySnapshotSetAddResult.Success)
                owner.Overflow(ref page, state, group);
        }

        internal void WriteCell(int column, in TerminalCell cell)
        {
            Write(column, GhosttySnapshotLivePage.EncodeStyle(in cell));
            int suffix = TerminalGraphemeStorage.SuffixLength(in cell);
            if (suffix == 0 || owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            if (state.Storage.Graphemes.Set(checked(Offset + column), suffix) == GhosttySnapshotGraphemeAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (owner.GrowGraphemes(ref page, state, group, layout) &&
                state.Storage.Graphemes.Set(checked(Offset + column), suffix) != GhosttySnapshotGraphemeAddResult.Success)
                owner.Overflow(ref page, state, group);
        }

        internal void AppendGrapheme(int column)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            if (state.Storage.Graphemes.Append(checked(Offset + column)) == GhosttySnapshotGraphemeAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (owner.GrowGraphemes(ref page, state, group, layout) &&
                state.Storage.Graphemes.Append(checked(Offset + column)) != GhosttySnapshotGraphemeAddResult.Success)
                owner.Overflow(ref page, state, group);
        }

        internal void Swap(int left, int right)
        {
            if (owner is null) return;
            state.Storage.Styles.SwapCells(checked(Offset + left), checked(Offset + right));
            state.Storage.Graphemes.Swap(checked(Offset + left), checked(Offset + right));
        }

        // True means an in-page ownership transfer. Full rows swap storage;
        // partial rows clear the destination, move the run, then zero the source.
        internal bool ShiftFrom(RowEdit source, int start, int count, bool wholeRow = true)
        {
            if (owner is null || count <= 0 ||
                row.SnapshotAllocation is not { MetadataOverflow: false } page) return false;
            if (source.Owner is null)
            {
                owner.Overflow(ref page, state, Group(rows, page));
                return false;
            }
            if (ReferenceEquals(row, source.Row)) return false;
            if (ReferenceEquals(state, source.AllocatorState))
            {
                if (!wholeRow) Clear(start, count);
                for (int i = 0; i < count; i++)
                {
                    state.Storage.Styles.SwapCells(checked(Offset + start + i), checked(source.Offset + start + i));
                    state.Storage.Graphemes.Swap(checked(Offset + start + i), checked(source.Offset + start + i));
                }
                return true;
            }
            // Source and destination are distinct rows, even when on one page.
            // Cross-page failures retry the entire destination range after a
            // rebuild; an already-copied prefix is released before retrying.
            while (true)
            {
                Clear(start, count);
                GhosttySnapshotSetAddResult result = GhosttySnapshotSetAddResult.Success;
                GhosttySnapshotGraphemeAddResult grapheme = GhosttySnapshotGraphemeAddResult.Success;
                for (int i = 0; i < count; i++)
                {
                    grapheme = state.Storage.Graphemes.CopyCellFrom(checked(Offset + start + i), source.AllocatorState.Storage.Graphemes,
                        checked(source.Offset + start + i));
                    if (grapheme != GhosttySnapshotGraphemeAddResult.Success) break;
                    result = state.Storage.Styles.CopyCellFrom(checked(Offset + start + i), source.AllocatorState.Storage.Styles,
                        checked(source.Offset + start + i));
                    if (result != GhosttySnapshotSetAddResult.Success) break;
                }
                if (result == GhosttySnapshotSetAddResult.Success && grapheme == GhosttySnapshotGraphemeAddResult.Success) return false;
                if (!(grapheme != GhosttySnapshotGraphemeAddResult.Success
                    ? owner.GrowGraphemes(ref page, state, Group(rows, page), layout)
                    : owner.Grow(ref page, state, Group(rows, page), result, layout))) return false;
            }
        }

        public void Dispose()
        {
            if (owner is not null) state.Revisions[row.SnapshotAllocationRow] = row.SnapshotMetadataRevision;
        }
    }
}
