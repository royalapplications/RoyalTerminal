// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using GhosttySelection = RoyalTerminal.GhosttySharp.GhosttySelection;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    /// <inheritdoc />
    public string? ReadBufferSelection(in TerminalSelectionRange selection, bool unwrap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryCreateNativeSelection(selection, out GhosttySelection native, absoluteSelection: true)) return null;
        using GhosttyFormatter formatter = new(_terminal, GhosttyVtNative.GhosttyFormatterFormat.Plain,
            unwrap: unwrap && !selection.Rectangle, trim: false, selection: native);
        return formatter.FormatToString();
    }

    /// <inheritdoc />
    public bool TryGetWordExtent(TerminalGridPosition position, ReadOnlySpan<char> delimiters,
        out TerminalWordExtent extent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        extent = default;
        ViewportScrollMapping mapping = GetViewportScrollMapping();
        if ((uint)position.Column >= (uint)_screen.Columns || position.Row < 0 ||
            (ulong)position.Row >= mapping.EffectiveTotalRows) return false;
        ulong nativeRow = mapping.EffectiveBaseOffsetRows + (ulong)position.Row;
        if (nativeRow > uint.MaxValue || !_terminal.TryGetGridReference(
            GhosttyVtNative.GhosttyPoint.Screen(checked((ushort)position.Column), (uint)nativeRow),
            out GhosttyVtNative.GhosttyGridRef reference)) return false;

        // Native accepts an exact scalar list; preserve the shared host's Unicode
        // White_Space policy, including non-ASCII separators, plus user delimiters.
        ReadOnlySpan<uint> whitespace = [9, 10, 11, 12, 13, 32, 0x85, 0xa0, 0x1680,
            0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008,
            0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000];
        int capacity = checked(whitespace.Length + delimiters.Length);
        uint[]? rented = null;
        Span<uint> boundaries = capacity <= 128
            ? stackalloc uint[capacity]
            : (rented = ArrayPool<uint>.Shared.Rent(capacity));
        try
        {
            whitespace.CopyTo(boundaries);
            int count = whitespace.Length;
            while (!delimiters.IsEmpty)
            {
                OperationStatus status = Rune.DecodeFromUtf16(delimiters, out Rune rune, out int consumed);
                if (status == OperationStatus.Done) boundaries[count++] = (uint)rune.Value;
                delimiters = delimiters[Math.Max(1, consumed)..];
            }

            if (!_terminal.TrySelectWord(in reference, boundaries[..count], out GhosttySelection selection) ||
                !_terminal.TryGetPointFromGridReference(selection.Start, GhosttyVtNative.GhosttyPointTag.Screen, out var start) ||
                !_terminal.TryGetPointFromGridReference(selection.End, GhosttyVtNative.GhosttyPointTag.Screen, out var end))
                return false;

            // Ghostty retains whole pages beyond a host's row budget. Selection
            // must not expose that hidden prefix, even when a word wraps into it.
            ulong firstRow = Math.Max(start.Y, mapping.EffectiveBaseOffsetRows);
            if (!TryMapNativeAbsoluteRowToEffective(mapping, firstRow, out int startRow) ||
                !TryMapNativeAbsoluteRowToEffective(mapping, end.Y, out int endRow)) return false;
            extent = new(new(firstRow == start.Y ? start.X : 0, startRow), new(end.X + 1, endRow));
            return true;
        }
        finally
        {
            if (rented is not null) ArrayPool<uint>.Shared.Return(rented, clearArray: true);
        }
    }
}
