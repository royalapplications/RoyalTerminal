// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void WidenGraphemeAcrossWrap(TerminalRow source, int sourceRow, int sourceColumn,
        in TerminalCell original, int codepoint)
    {
        ClearPreservedCellsForMutation(source);
        ClearRasterGraphicsForTextMutation(sourceRow, sourceColumn, 1);
        bool atScreenEdge = sourceColumn == _screen.Columns - 1;
        bool hasSuffix = !string.IsNullOrEmpty(original.Grapheme);
        TerminalCell spacer = original;
        if (!hasSuffix) WriteCellFromPen(ref spacer, 0, 0);
        spacer.Codepoint = 0;
        spacer.Width = atScreenEdge ? (byte)0 : (byte)1;
        spacer.IsWideSpacerHead = atScreenEdge;
        if (hasSuffix)
        {
            // Keep the old suffix, style and allocation on this temporary
            // spacer through scrolling. Native print does not erase/reclone it.
            using GhosttySnapshotPageTracker.RowEdit metadata = _screen.EditSnapshotRowMetadata(source);
            source[sourceColumn] = spacer;
        }
        else WriteStyledCell(source, sourceColumn, in spacer);
        source.IsDirty = true;

        _delayedWrap = false;
        LineFeed(wrapForced: atScreenEdge, softWrap: true);
        _cursorCol = _scrollLeft;
        TerminalRow destination = _screen.GetViewportRow(_cursorRow);
        ClearPreservedCellsForMutation(destination);
        ClearRasterGraphicsForTextMutation(_cursorRow, _scrollLeft, 2);
        WriteWidenedGraphemeCell(destination, _scrollLeft, original.Codepoint, 2);

        // Like Ghostty's new_pin.up(1), resolve the source AFTER scrolling:
        // row objects may have exchanged payloads, entered history or recycled.
        // Do not resurrect the captured string if the source no longer exists.
        int previous = _screen.GetAbsoluteRowForViewportRow(_cursorRow) - 1;
        if (hasSuffix)
        {
            // Index can be clamped at the physical bottom outside the
            // scrolling region. Preserve the host's same-row wrap behavior
            // rather than adopting unrelated metadata from the row above.
            // A recycled row fails this payload identity check.
            TerminalRow? surviving = ReferenceEquals(source, destination) && HasTransferredSpacer(source, sourceColumn, original.Grapheme!)
                ? source : previous >= 0 ? _screen.GetRow(previous) : null;
            if (surviving is not null && HasTransferredSpacer(surviving, sourceColumn, original.Grapheme!))
            {
                using GhosttySnapshotPageTracker.RowEdit sourceMetadata = _screen.EditSnapshotRowMetadata(surviving);
                using GhosttySnapshotPageTracker.RowEdit destinationMetadata = _screen.EditSnapshotRowMetadata(destination);
                destinationMetadata.TransferGraphemeFrom(sourceMetadata, sourceColumn, _scrollLeft);
                destination[_scrollLeft].Grapheme = original.Grapheme;
                surviving[sourceColumn].Grapheme = null;
                surviving.IsDirty = true;
            }
        }

        // The tail uses the current pen and can replace the page. Complete it
        // before the final suffix append; never hold a writable cell reference
        // or an unstamped metadata edit across those operations.
        WriteWidenedGraphemeCell(destination, _scrollLeft + 1, 0, 0);
        AdvanceCursorAfterGraphic(2);
        using (GhosttySnapshotPageTracker.RowEdit metadata = _screen.EditSnapshotRowMetadata(destination))
        {
            TerminalCell moved = destination.ReadOnlyCells[_scrollLeft];
            Span<char> baseScalar = stackalloc char[2];
            int baseLength = new Rune(moved.Codepoint).EncodeToUtf16(baseScalar);
            ReadOnlySpan<char> oldSuffix = moved.Grapheme is { } grapheme
                ? grapheme.AsSpan(char.IsHighSurrogate(grapheme[0]) ? 2 : 1) : default;
            Span<char> suffix = stackalloc char[2];
            int length = new Rune(codepoint).EncodeToUtf16(suffix);
            // printCell can remap the base if the charset changed between
            // scalars. Only suffixes are transferred, not the old inline base.
            moved.Grapheme = string.Concat(baseScalar[..baseLength], oldSuffix, suffix[..length]);
            metadata.AppendGrapheme(_scrollLeft);
            destination[_scrollLeft] = moved;
        }
        destination.IsDirty = true;
    }

    private static bool HasTransferredSpacer(TerminalRow? row, int column, string grapheme)
        => row is not null && row.ReadOnlyCells[column].Codepoint == 0 && ReferenceEquals(row.ReadOnlyCells[column].Grapheme, grapheme);

    private void WriteWidenedGraphemeCell(TerminalRow row, int column, int codepoint, byte width)
    {
        // Match printCell's width-change cleanup, without erasing an unchanged
        // wide head/tail or releasing the tail's metadata before transfer.
        TerminalCell previous = row.ReadOnlyCells[column];
        if (previous.Width != width && (previous.Width == 2 || previous.Width == 0 && !previous.IsWideSpacerHead))
        {
            int neighbor = previous.Width == 2 ? column + 1 : column - 1;
            if ((uint)neighbor < (uint)row.Columns) EraseCell(row, neighbor, CreateErasedCell());
            if (column <= 1) ClearPreviousWideSpacerHead();
        }
        WriteCellFromPen(row, column, codepoint, width);
    }
}
