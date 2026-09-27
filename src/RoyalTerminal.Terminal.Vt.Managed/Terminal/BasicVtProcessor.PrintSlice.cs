// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Numerics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Unicode;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Ghostty Terminal.printSlice/printRepeat: bounded decoded storage, row-local
    // template writes, and scalar fallback at complex cells or grapheme boundaries.
    // TerminalCell contains managed references, so native packed-u64 SIMD stores
    // cannot be transplanted. Generic specialization keeps ASCII decoding-free.
    private void PrintSlice<T>(ReadOnlySpan<T> codepoints) where T : unmanaged, IBinaryInteger<T>
    {
        if (codepoints.Length < 2 || _statusDisplay != 0 || _insertMode || !_autoWrap ||
            _currentHyperlinkId != 0)
        {
            foreach (T codepoint in codepoints) PutChar(int.CreateTruncating(codepoint));
            return;
        }
        for (int index = 0; index < codepoints.Length;)
        {
            // A single shift affects one graphic only. Resume batching in the
            // same input span once the scalar printer consumes it (#14356).
            int consumed = _charsets.HasSingleShift ? 0 : TryPrintSlice(codepoints[index..]);
            if (consumed == 0) PutChar(int.CreateTruncating(codepoints[index++]));
            else index += consumed;
        }
    }

    private int PrintUtf8Slice(ReadOnlySpan<byte> data)
    {
        Span<int> decoded = stackalloc int[256];
        int bytes = 0, count = 0;
        while (bytes < data.Length && count < decoded.Length)
        {
            byte next = data[bytes];
            if (next < 0x20 || next == 0x7F) break;
            if (Rune.DecodeFromUtf8(data[bytes..], out Rune rune, out int consumed) != OperationStatus.Done) break;
            // Match ProcessGround: decoded C1 controls are ignored, not printed
            // and not executed as raw control-string introductions.
            if (rune.Value is < 0x80 or > 0x9F) decoded[count++] = rune.Value;
            bytes += consumed;
        }
        PrintSlice<int>(decoded[..count]);
        return bytes; // Incomplete/invalid input remains with the streaming decoder.
    }

    private void PrintRepeat(int count)
    {
        Span<int> repeated = stackalloc int[Math.Min(count, 256)];
        repeated.Fill(_lastGraphicCodepoint);
        while (count > 0)
        {
            int length = Math.Min(count, repeated.Length);
            PrintSlice<int>(repeated[..length]);
            count -= length;
        }
    }

    private int TryPrintSlice<T>(ReadOnlySpan<T> codepoints) where T : unmanaged, IBinaryInteger<T>
    {
        try { return TryPrintSliceCore<T>(codepoints); }
        catch (OutOfMemoryException failure) when (_screen.RecordSnapshotMutationFailure(failure)) { throw; }
    }

    private int TryPrintSliceCore<T>(ReadOnlySpan<T> codepoints) where T : unmanaged, IBinaryInteger<T>
    {
        if (codepoints.Length < 2) return 0;
        if (_screen.TracksSnapshotMetadata) ApplySnapshotCursorStyleDrops();
        ClampCursor();

        int first = int.CreateTruncating(codepoints[0]);
        int charset = _charsets.GlSet;
        bool mapCharset = charset > 1;
        // Non-identity charsets retain the scalar Unicode/grapheme behavior:
        // width is calculated before mapping, and single shifts consume there.
        if (mapCharset && first > 255) return 0;
        int width = first <= 255 ? 1 : TerminalCellWidthCalculator.GetCodepointWidth(first);
        if (width is not (1 or 2) || first == 0x10EEEE) return 0;
        bool clusters = _extendedDecModes.Contains(ManagedDecModeFlag.GraphemeClusters);
        if (first > 255 && clusters)
        {
            if (_scrollLeft != 0 || _delayedWrap) return 0;
            if (_cursorCol > 0)
            {
                TerminalRow previous = _screen.GetViewportRow(_cursorRow);
                int left = _cursorCol - 1;
                if (left >= previous.Columns) return 0;
                if (previous.ReadOnlyCells[left].Width == 0 && left > 0) left--;
                ref readonly TerminalCell cell = ref previous.ReadOnlyCells[left];
                if (cell.Grapheme is not null ||
                    (cell.Codepoint != 0 && !IsPrintGraphemeBreak(cell.Codepoint, first))) return 0;
            }
        }

        ConsumeDelayedWrapBeforePrint();
        ClampCursor();
        if ((uint)_cursorRow >= (uint)_screen.ViewportRows || (uint)_cursorCol >= (uint)_screen.Columns) return 0;
        TerminalRow row = _screen.GetViewportRow(_cursorRow);
        int available = Math.Min(row.Columns, CursorRightLimit + 1) - _cursorCol;
        int limit = Math.Min(available / width, codepoints.Length);
        if (limit <= 0) return 0; // Scalar handles wide spacers and one-column screens.

        ReadOnlySpan<TerminalCell> old = row.ReadOnlyCells;
        int count = 0, previousCodepoint = first;
        for (; count < limit; count++)
        {
            int codepoint = int.CreateTruncating(codepoints[count]);
            if (mapCharset && codepoint > 255) break;
            if (typeof(T) != typeof(byte) && count != 0)
            {
                int nextWidth = codepoint <= 255 ? 1 : TerminalCellWidthCalculator.GetCodepointWidth(codepoint);
                if (nextWidth != width || codepoint == 0x10EEEE ||
                    (codepoint > 255 && clusters && (_scrollLeft != 0 || !IsPrintGraphemeBreak(previousCodepoint, codepoint)))) break;
            }
            int column = _cursorCol + count * width;
            if (!IsSimplePrintCell(in old[column]) || (width == 2 && !IsSimplePrintCell(in old[column + 1]))) break;
            previousCodepoint = codepoint;
        }
        if (count == 0) return 0;

        ClearPreservedCellsForMutation(row);
        ClearRasterGraphicsForTextMutation(_cursorRow, _cursorCol, count * width);
        if (_screen.TracksSnapshotMetadata) RecordSnapshotCursorStyle(CaptureSnapshotPen());
        using GhosttySnapshotPageTracker.RowEdit metadata = _screen.EditSnapshotRowMetadata(row);
        ApplySnapshotCursorStyleDrops();
        TerminalCell template = default;
        WriteCellFromPen(ref template, 0, (byte)width);
        // Detach COW storage before committing allocator references. This is
        // the same single destination allocation the write already required.
        Span<TerminalCell> destination = row.Cells.Slice(_cursorCol, count * width);
        metadata.WriteSimpleCursorRun(_cursorCol, count * width);
        if (width == 1)
        {
            if (mapCharset)
            {
                for (int index = 0; index < count; index++)
                {
                    template.Codepoint = ManagedCharsetState.MapCodepoint(int.CreateTruncating(codepoints[index]), charset);
                    destination[index] = template;
                }
            }
            else
            {
                for (int index = 0; index < count; index++)
                {
                    template.Codepoint = int.CreateTruncating(codepoints[index]);
                    destination[index] = template;
                }
            }
        }
        else
        {
            TerminalCell tail = template;
            tail.Width = 0;
            for (int index = 0; index < count; index++)
            {
                template.Codepoint = int.CreateTruncating(codepoints[index]);
                destination[index * 2] = template;
                destination[index * 2 + 1] = tail;
            }
        }
        row.IsDirty = true;
        AdvanceCursorAfterGraphic(count * width);
        _lastGraphicCodepoint = int.CreateTruncating(codepoints[count - 1]);
        return count;
    }

    private static bool IsSimplePrintCell(in TerminalCell cell)
        => cell.Width == 1 && !cell.IsWideSpacerHead && cell.Grapheme is null && cell.HyperlinkId == 0;

    private static bool IsPrintGraphemeBreak(int previous, int current)
    {
        ReadOnlySpan<uint> pair = [(uint)previous, (uint)current];
        return TerminalCellWidthCalculator.GetFirstGraphemeWidth(pair, out _) == 1;
    }
}
