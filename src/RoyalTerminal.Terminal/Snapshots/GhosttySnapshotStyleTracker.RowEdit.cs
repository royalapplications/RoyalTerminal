// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotStyleTracker
{
    internal RowEdit EditRow(TerminalRowBuffer rows, TerminalRow row, GhosttySnapshotAllocation layout)
    {
        if (row.SnapshotAllocation is not { MetadataOverflow: false } page) return default;
        State state;
        if (_pages.TryGetValue(page, out State? known) &&
            known.Revisions.TryGetValue(row.SnapshotAllocationRow, out ulong? revision) && revision == row.SnapshotStyleRevision)
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
    // cover every style-changing cell in the scope. Untracked screens use default.
    internal readonly struct RowEdit(GhosttySnapshotStyleTracker? owner, TerminalRowBuffer rows,
        TerminalRow row, State state, GhosttySnapshotAllocation layout) : IDisposable
    {
        private GhosttySnapshotStyleTracker? Owner => owner;
        private TerminalRow Row => row;
        private State AllocatorState => state;
        private int Offset => checked(row.SnapshotAllocationRow * row.SnapshotAllocation!.Capacity.Columns);

        internal void Clear(int start, int count)
        {
            if (owner is not null && count > 0) state.Storage.ClearCells(checked(Offset + start), count);
        }

        internal void Write(int column, GhosttySnapshotStyle style)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            GhosttySnapshotSetAddResult result = state.Storage.ChangeCell(checked(Offset + column), style);
            if (result == GhosttySnapshotSetAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (owner.Grow(ref page, state, group, result, layout) &&
                state.Storage.ChangeCell(checked(Offset + column), style) != GhosttySnapshotSetAddResult.Success)
                owner.Overflow(ref page, state, group);
        }

        internal void Swap(int left, int right)
        {
            if (owner is not null) state.Storage.SwapCells(checked(Offset + left), checked(Offset + right));
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
                if (!wholeRow) state.Storage.ClearCells(checked(Offset + start), count);
                for (int i = 0; i < count; i++) state.Storage.SwapCells(checked(Offset + start + i), checked(source.Offset + start + i));
                return true;
            }
            // Source and destination are distinct rows, even when on one page.
            // Cross-page failures retry the entire destination range after a
            // rebuild; an already-copied prefix is released before retrying.
            while (true)
            {
                state.Storage.ClearCells(checked(Offset + start), count);
                GhosttySnapshotSetAddResult result = GhosttySnapshotSetAddResult.Success;
                for (int i = 0; i < count; i++)
                {
                    result = state.Storage.CopyCellFrom(checked(Offset + start + i), source.AllocatorState.Storage,
                        checked(source.Offset + start + i));
                    if (result != GhosttySnapshotSetAddResult.Success) break;
                }
                if (result == GhosttySnapshotSetAddResult.Success) return false;
                if (!owner.Grow(ref page, state, Group(rows, page), result, layout)) return false;
            }
        }

        public void Dispose()
        {
            if (owner is not null) state.Revisions[row.SnapshotAllocationRow] = row.SnapshotStyleRevision;
        }
    }
}
