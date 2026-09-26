// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    internal RowEdit EditRow(TerminalRowBuffer rows, TerminalRow row, GhosttySnapshotAllocation layout, TerminalScreen? screen = null)
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
            state.Storage.Hyperlinks.ClearCells(checked(Offset + start), count);
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
                owner.ObserveHyperlink(ref page, state, group, index, encoded, layout);
        }

        internal int WriteCursorHyperlink(int column, int token)
        {
            if (owner is null || row.SnapshotAllocation is not { MetadataOverflow: false } page) return token;
            int index = checked(Offset + column);
            while (state.Storage.Hyperlinks.WriteCursorToCell(index) == GhosttySnapshotHyperlinkAddResult.MapFull)
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

        // Terminal.print's widening wrap is not a row clone: within one page
        // it moves the slice, while across pages it appends each old suffix
        // before releasing the source. Replacement scratch and any growth must
        // therefore occur with the source still live. The destination is the
        // newly printed base (no suffix); callers commit both payloads next.
        internal bool TryTransferGraphemeFrom(RowEdit source, int sourceColumn, int destinationColumn,
            int suffixLength, out int copied)
        {
            copied = suffixLength;
            if (owner is not null && row.SnapshotAllocation is { MetadataOverflow: false } page)
            {
                if (source.Owner is null)
                    owner.Overflow(ref page, state, Group(rows, page));
                else if (ReferenceEquals(state, source.AllocatorState))
                    state.Storage.Graphemes.Swap(checked(source.Offset + sourceColumn), checked(Offset + destinationColumn));
                else
                {
                    int length = source.AllocatorState.Storage.Graphemes.SuffixLength(checked(source.Offset + sourceColumn));
                    for (copied = 0; copied < length; copied++)
                        if (!TryAppendGrapheme(destinationColumn)) return false;
                }
            }
            // Even unrepresentable destinations keep rendering; the source's
            // visible suffix is removed, so its independently tracked slice
            // must be released too. Same-page moves have already removed it.
            if (source.Owner is not null)
                source.AllocatorState.Storage.Graphemes.Clear(checked(source.Offset + sourceColumn));
            return true;
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
                if (!(grapheme != GhosttySnapshotGraphemeAddResult.Success
                    ? owner.GrowGraphemes(ref page, state, Group(rows, page), layout)
                    : hyperlink != GhosttySnapshotHyperlinkAddResult.Success
                    ? owner.GrowMetadata(ref page, state, Group(rows, page), GhosttySnapshotHyperlinkStorage.GrowthDimension(hyperlink), layout)
                    : owner.Grow(ref page, state, Group(rows, page), result, layout))) return false;
            }
        }

        public void Dispose()
        {
            if (owner is not null) state.Revisions[row.SnapshotAllocationRow] = row.SnapshotMetadataRevision;
        }
    }
}
