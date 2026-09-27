// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    /// <inheritdoc />
    public bool TryGetLineExtent(TerminalGridPosition position, ReadOnlySpan<uint> whitespace,
        bool semanticPromptBoundary, out TerminalLineExtent extent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        extent = default;
        ViewportScrollMapping mapping = GetViewportScrollMapping();
        if ((uint)position.Column >= (uint)_screen.Columns || position.Row < 0 ||
            (ulong)position.Row >= mapping.EffectiveTotalRows) return false;
        ulong nativeRow = mapping.EffectiveBaseOffsetRows + (ulong)position.Row;
        if (nativeRow > uint.MaxValue || !_terminal.TryGetGridReference(
            GhosttyVtNative.GhosttyPoint.Screen((ushort)position.Column, (uint)nativeRow), out var reference) ||
            !_terminal.TrySelectLine(in reference, whitespace, semanticPromptBoundary, out GhosttySelection selection) ||
            !_terminal.TryGetPointFromGridReference(selection.Start, GhosttyVtNative.GhosttyPointTag.Screen, out var start) ||
            !_terminal.TryGetPointFromGridReference(selection.End, GhosttyVtNative.GhosttyPointTag.Screen, out var end))
            return false;

        ulong firstRow = start.Y;
        int firstColumn = start.X;
        if (firstRow < mapping.EffectiveBaseOffsetRows)
        {
            // Native pages may retain a prefix outside the configured row budget.
            // Re-trim at that host boundary so hidden text cannot keep visible
            // leading whitespace selected or leak inaccessible coordinates.
            firstRow = mapping.EffectiveBaseOffsetRows;
            firstColumn = 0;
            ReadOnlySpan<uint> trim = whitespace.IsEmpty ? [0, 32, 9] : whitespace;
            while (firstRow < end.Y || (firstRow == end.Y && firstColumn <= end.X))
            {
                if (firstRow > uint.MaxValue || !TryReadLineCell((ushort)firstColumn, (uint)firstRow,
                    out uint codepoint, out _, out bool hasText)) return false;
                if (hasText && !trim.Contains(codepoint)) break;
                if (++firstColumn == _screen.Columns) { firstColumn = 0; firstRow++; }
            }
            if (firstRow > end.Y || (firstRow == end.Y && firstColumn > end.X)) return false;
        }

        if (!TryMapNativeAbsoluteRowToEffective(mapping, firstRow, out int startRow) ||
            !TryMapNativeAbsoluteRowToEffective(mapping, end.Y, out int endRow) ||
            !TryReadLineCell(end.X, end.Y, out _, out byte width, out _)) return false;
        extent = new(new(firstColumn, startRow), new(Math.Min(_screen.Columns, end.X + width), endRow));
        return true;
    }

    private unsafe bool TryReadLineCell(ushort column, uint row, out uint codepoint, out byte width, out bool hasText)
    {
        codepoint = 0;
        width = 1;
        hasText = false;
        if (!_terminal.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Screen(column, row), out var reference) ||
            GhosttyVtNative.GridRefCell(in reference, out ulong raw) != GhosttyVtNative.GhosttyResult.Success) return false;
        uint cp = 0;
        bool text = false;
        GhosttyVtNative.GhosttyCellWide wide = default;
        if (GhosttyVtNative.CellGet(raw, GhosttyVtNative.GhosttyCellData.Codepoint, &cp) != GhosttyVtNative.GhosttyResult.Success ||
            GhosttyVtNative.CellGet(raw, GhosttyVtNative.GhosttyCellData.HasText, &text) != GhosttyVtNative.GhosttyResult.Success ||
            GhosttyVtNative.CellGet(raw, GhosttyVtNative.GhosttyCellData.Wide, &wide) != GhosttyVtNative.GhosttyResult.Success)
            return false;
        codepoint = cp;
        hasText = text;
        width = wide == GhosttyVtNative.GhosttyCellWide.Wide ? (byte)2 : (byte)1;
        return true;
    }
}
