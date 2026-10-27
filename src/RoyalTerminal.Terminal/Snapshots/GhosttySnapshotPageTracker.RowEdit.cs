// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    internal RowEdit EditRow(TerminalRowBuffer rows, TerminalRow row, GhosttySnapshotAllocation layout, TerminalScreen? screen = null)
    {
        ThrowIfMutationFailed();
        if (row.SnapshotAllocation is not { MetadataOverflow: false } page) return default;
        State state;
        if (_pages.TryGetValue(page, out State? known) &&
            known.Revisions.TryGetValue(row.SnapshotAllocationRow, out ulong? revision) && revision == row.SnapshotMetadataRevision)
            state = Exclusive(page, known);
        else
        {
            using PageRowsLease lease = RentGroup(rows, page);
            List<TerminalRow> group = lease.Rows;
            state = Writable(page, group);
            if (!Synchronize(ref page, state, group, layout, screen)) return default;
        }
        return new(this, rows, row, state, layout, screen);
    }

    // Stack-only by usage: no per-edit closure, journal or dense cell snapshot.
    // Callers edit allocator references before the corresponding cells and must
    // cover every metadata-changing cell in the scope. Untracked screens use default.
    internal readonly struct RowEdit(GhosttySnapshotPageTracker? owner, TerminalRowBuffer rows,
        TerminalRow row, State state, GhosttySnapshotAllocation layout, TerminalScreen? screen) : IDisposable
    {
        private GhosttySnapshotPageTracker? Owner => owner;
        private TerminalRow Row => row;
        private State AllocatorState => state;
        private int Offset => checked(row.SnapshotAllocationRow * row.SnapshotAllocation!.Capacity.Columns);

        internal void Clear(int start, int count)
        {
            if (owner is null || count <= 0) return;
            state.Storage.Graphemes.ClearCells(checked(Offset + start), count);
            screen?.MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.MetadataClear);
            state.Storage.Hyperlinks.ClearCells(checked(Offset + start), count);
            state.Storage.Styles.ClearCells(checked(Offset + start), count);
        }

        internal void ClearGrapheme(int column)
        {
            if (owner is not null) state.Storage.Graphemes.Clear(checked(Offset + column));
        }

        // The printer has checked that these cells contain no grapheme/link
        // metadata and installed the accepted cursor pen before opening the edit.
        internal void WriteSimpleCursorRun(int start, int count)
        {
            if (owner is null) return;
            state.Storage.Styles.WriteCursorToCells(checked(Offset + start), count);
            screen?.MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.MetadataWrite);
        }

        internal void Write(int column, GhosttySnapshotStyle style)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            state.Storage.Graphemes.Clear(checked(Offset + column));
            GhosttySnapshotSetAddResult result = state.Storage.Styles.ChangeCell(checked(Offset + column), style);
            screen?.MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.MetadataWrite);
            if (result == GhosttySnapshotSetAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (owner.Grow(ref page, state, group, result, layout) &&
                state.Storage.Styles.ChangeCell(checked(Offset + column), style) != GhosttySnapshotSetAddResult.Success)
                owner.Overflow(ref page, state, group);
        }

        internal void WriteCell(int column, in TerminalCell cell)
        {
            Write(column, GhosttySnapshotLivePage.EncodeStyle(in cell));
            WriteHyperlink(column, cell.HyperlinkId);
            int suffix = TerminalGraphemeStorage.SuffixLength(in cell);
            if (suffix == 0 || owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            if (state.Storage.Graphemes.Set(checked(Offset + column), suffix) == GhosttySnapshotGraphemeAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (owner.GrowGraphemes(ref page, state, group, layout) &&
                state.Storage.Graphemes.Set(checked(Offset + column), suffix) != GhosttySnapshotGraphemeAddResult.Success)
                owner.Overflow(ref page, state, group);
        }

        internal void WriteHyperlink(int column, int token)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return;
            byte[]? encoded = screen?.SnapshotHyperlinkEncoding(token);
            int index = checked(Offset + column);
            if (token != 0 && encoded is null) { owner.Overflow(ref page, state, Group(rows, page)); return; }
            GhosttySnapshotHyperlinkAddResult result = state.Storage.Hyperlinks.ObserveCell(index, encoded);
            if (result == GhosttySnapshotHyperlinkAddResult.Success) return;
            List<TerminalRow> group = Group(rows, page);
            if (result == GhosttySnapshotHyperlinkAddResult.InvalidEntry) { owner.Overflow(ref page, state, group); return; }
            if (owner.GrowMetadata(ref page, state, group, GhosttySnapshotHyperlinkStorage.GrowthDimension(result), layout))
                owner.ObserveHyperlink(ref page, state, group, row, column, encoded, layout);
        }

        internal int WriteCursorHyperlink(int column, int token)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return token;
            while (state.Storage.Hyperlinks.WriteCursorToCell(checked(Offset + column)) == GhosttySnapshotHyperlinkAddResult.MapFull)
            {
                List<TerminalRow> group = Group(rows, page);
                // Screen.cursorSetHyperlink reserves URI-only scratch before
                // map growth. A successful reservation dies with the old page.
                while (!state.Storage.Hyperlinks.TryReserveCursorUri())
                    if (!owner.GrowMetadata(ref page, state, group, GhosttySnapshotCapacityDimension.StringBytes,
                        layout, preserveOnFailure: true)) return 0;
                if (!owner.GrowMetadata(ref page, state, group, GhosttySnapshotCapacityDimension.HyperlinkBytes,
                    layout, preserveOnFailure: true)) return 0;
            }
            return state.Storage.Hyperlinks.CursorId == 0 ? 0 : token;
        }

        internal bool TryAppendGrapheme(int column)
        {
            bool accepted = TryAppendGraphemeCore(column);
            if (accepted) screen?.MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.GraphemeAppend);
            return accepted;
        }

        private bool TryAppendGraphemeCore(int column)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return true;
            if (state.Storage.Graphemes.Append(checked(Offset + column)) == GhosttySnapshotGraphemeAddResult.Success) return true;
            List<TerminalRow> group = Group(rows, page);
            // Screen.appendGrapheme retries once, retaining the old suffix on
            // either failure. No payload is committed by the caller on failure,
            // so the remaining page is still exactly representable.
            return owner.GrowMetadata(ref page, state, group, GhosttySnapshotCapacityDimension.GraphemeBytes,
                layout, preserveOnFailure: true) &&
                state.Storage.Graphemes.Append(checked(Offset + column)) == GhosttySnapshotGraphemeAddResult.Success;
        }

        // Screen.setGraphemes restores a captured suffix after a widening wrap.
        // Unlike repeated append, it allocates the entire slice once and retries
        // after growing the cursor page. The replaced source is already erased.
        internal bool TrySetGraphemes(int column, int suffixLength)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return true;
            if (state.Storage.Graphemes.Set(checked(Offset + column), suffixLength) == GhosttySnapshotGraphemeAddResult.Success) return true;
            List<TerminalRow> group = Group(rows, page);
            return owner.GrowMetadata(ref page, state, group, GhosttySnapshotCapacityDimension.GraphemeBytes,
                layout, preserveOnFailure: true) &&
                state.Storage.Graphemes.Set(checked(Offset + column), suffixLength) == GhosttySnapshotGraphemeAddResult.Success;
        }

        internal void Swap(int left, int right)
        {
            if (owner is null) return;
            state.Storage.Styles.SwapCells(checked(Offset + left), checked(Offset + right));
            state.Storage.Graphemes.Swap(checked(Offset + left), checked(Offset + right));
            state.Storage.Hyperlinks.Swap(checked(Offset + left), checked(Offset + right));
        }

        // True means an in-page ownership transfer. Full rows swap storage;
        // partial rows clear the destination, move the run, then zero the source.
        internal bool ShiftFrom(RowEdit source, int start, int count, bool wholeRow = true)
        {
            owner?.ThrowIfMutationFailed();
            try { return ShiftFromCore(source, start, count, wholeRow); }
            catch (Exception failure) when ((failure is OutOfMemoryException or InvalidOperationException) && RecordFailure(failure))
            {
                throw;
            }
        }

        private bool ShiftFromCore(RowEdit source, int start, int count, bool wholeRow)
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
                    state.Storage.Hyperlinks.Swap(checked(Offset + start + i), checked(source.Offset + start + i));
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
                GhosttySnapshotHyperlinkAddResult hyperlink = GhosttySnapshotHyperlinkAddResult.Success;
                for (int i = 0; i < count; i++)
                {
                    grapheme = state.Storage.Graphemes.CopyCellFrom(checked(Offset + start + i), source.AllocatorState.Storage.Graphemes,
                        checked(source.Offset + start + i));
                    if (grapheme != GhosttySnapshotGraphemeAddResult.Success) break;
                    hyperlink = state.Storage.Hyperlinks.CopyCellFrom(checked(Offset + start + i), source.AllocatorState.Storage.Hyperlinks,
                        checked(source.Offset + start + i));
                    if (hyperlink != GhosttySnapshotHyperlinkAddResult.Success) break;
                    result = state.Storage.Styles.CopyCellFrom(checked(Offset + start + i), source.AllocatorState.Storage.Styles,
                        checked(source.Offset + start + i));
                    if (result != GhosttySnapshotSetAddResult.Success) break;
                }
                if (result == GhosttySnapshotSetAddResult.Success && grapheme == GhosttySnapshotGraphemeAddResult.Success &&
                    hyperlink == GhosttySnapshotHyperlinkAddResult.Success) return false;
                GhosttySnapshotCapacityDimension? dimension = grapheme != GhosttySnapshotGraphemeAddResult.Success
                    ? GhosttySnapshotCapacityDimension.GraphemeBytes
                    : hyperlink != GhosttySnapshotHyperlinkAddResult.Success
                    ? GhosttySnapshotHyperlinkStorage.GrowthDimension(hyperlink)
                    : result == GhosttySnapshotSetAddResult.OutOfMemory ? GhosttySnapshotCapacityDimension.Styles : null;
                // Returning false here means a successful cross-page copy to
                // the caller. Exhaustion is fatal, not an overflow checkpoint
                // followed by copying the entire unaccepted source payload.
                if (!owner.GrowMetadata(ref page, state, Group(rows, page), dimension, layout, preserveOnFailure: true))
                    throw RowCopyFailed();
            }
        }

        public void Dispose()
        {
            if (owner is not { MutationFailed: false }) return;
            try
            {
                screen?.MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.Revision);
                state.Revisions[row.SnapshotAllocationRow] = row.SnapshotMetadataRevision;
            }
            catch (OutOfMemoryException failure) when (RecordFailure(failure)) { throw; }
        }

        private bool RecordFailure(Exception failure)
        {
            owner?.RecordMutationFailure(failure);
            return screen?.RecordSnapshotMutationFailure(failure) ?? true;
        }
    }
}
