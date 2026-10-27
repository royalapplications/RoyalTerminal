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
        // Capture the immutable suffix before replacing the source. Scrolling
        // can recycle its row, including the only row of an alternate screen.
        string? suffixBeforeWrap = original.Grapheme;
        WriteCellFromPen(source, sourceColumn, 0, atScreenEdge ? (byte)0 : (byte)1);
        using (GhosttySnapshotPageTracker.RowEdit metadata = _screen.EditSnapshotRowMetadata(source))
            source[sourceColumn].IsWideSpacerHead = atScreenEdge;
        source.IsDirty = true;

        _delayedWrap = false;
        LineFeed(wrapForced: atScreenEdge, softWrap: true);
        _cursorCol = _scrollLeft;
        TerminalRow destination = GetActiveRow(_cursorRow);
        ClearPreservedCellsForMutation(destination);
        ClearRasterGraphicsForTextMutation(_cursorRow, _scrollLeft, 2);
        WriteWidenedGraphemeCell(destination, _scrollLeft, original.Codepoint, 2);

        if (!string.IsNullOrEmpty(suffixBeforeWrap))
        {
            using GhosttySnapshotPageTracker.RowEdit metadata = _screen.EditSnapshotRowMetadata(destination);
            if (!metadata.TrySetGraphemes(_scrollLeft, TerminalGraphemeStorage.SuffixLength(in original))) return;
            destination[_scrollLeft].Grapheme = CopyGraphemeSuffix(
                destination.ReadOnlyCells[_scrollLeft].Codepoint, suffixBeforeWrap);
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
            if (metadata.TryAppendGrapheme(_scrollLeft))
            {
                moved.Grapheme = string.Concat(baseScalar[..baseLength], oldSuffix, suffix[..length]);
                destination[_scrollLeft] = moved;
            }
        }
        destination.IsDirty = true;
    }

    private static string CopyGraphemeSuffix(int codepoint, string original)
    {
        if (Rune.GetRuneAt(original, 0).Value == codepoint) return original;
        int start = char.IsHighSurrogate(original[0]) ? 2 : 1;
        Span<char> scalar = stackalloc char[2];
        int length = new Rune(codepoint).EncodeToUtf16(scalar);
        return string.Concat(scalar[..length], original.AsSpan(start));
    }

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
