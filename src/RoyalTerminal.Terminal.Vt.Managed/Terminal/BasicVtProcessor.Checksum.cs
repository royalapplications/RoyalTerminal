// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalChecksumPolicy
{
    private TerminalChecksumFlags _defaultChecksumFlags;
    private TerminalChecksumFlags _checksumFlags;

    /// <inheritdoc />
    public bool ChecksumReportsEnabled { get; set; }

    /// <inheritdoc />
    public TerminalChecksumFlags DefaultChecksumFlags
    {
        get => _defaultChecksumFlags;
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((byte)value, (byte)31);
            _defaultChecksumFlags = _checksumFlags = value;
        }
    }

    private void HandleChecksum(char intermediate)
    {
        if (!ChecksumReportsEnabled) return;
        if (intermediate == '#')
        {
            if (_params.Count <= 1) _checksumFlags = (TerminalChecksumFlags)((_params.Count == 0 ? 0 : _params[0]) & 31);
            return;
        }
        if (_params.Count > 6 || ResponseCallback is null) return;
        int id = _params.Count > 0 ? _params[0] : 0;
        int top = ChecksumCoordinate(2, 1, _originMode ? _scrollTop : 0, _screen.ViewportRows);
        int left = ChecksumCoordinate(3, 1, _originMode ? _scrollLeft : 0, _screen.Columns);
        int bottom = ChecksumCoordinate(4, _screen.ViewportRows, _originMode ? _scrollTop : 0, _screen.ViewportRows);
        int right = ChecksumCoordinate(5, _screen.Columns, _originMode ? _scrollLeft : 0, _screen.Columns);
        ushort checksum = ComputeChecksum(top, left, bottom, right);
        ResponseCallback(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"\u001bP{id}!~{checksum:X4}\u001b\\")));
    }

    private int ChecksumCoordinate(int index, int fallback, int origin, int maximum)
    {
        int value = index < _params.Count ? _params[index] : 0;
        return Math.Clamp((value == 0 ? fallback : value) + origin, 1, maximum) - 1;
    }

    private ushort ComputeChecksum(int top, int left, int bottom, int right)
    {
        bool noTrim = (_checksumFlags & TerminalChecksumFlags.NoTrim) != 0;
        bool full = (_checksumFlags & TerminalChecksumFlags.Full) != 0;
        bool countUndrawn = noTrim || (_checksumFlags & TerminalChecksumFlags.Undrawn) != 0;
        bool first = true;
        uint sum = 0;
        for (int y = top; y <= bottom; y++)
        {
            ReadOnlySpan<TerminalCell> cells = GetActiveRow(y).ReadOnlyCells;
            for (int x = left; x <= right; x++)
            {
                ref readonly TerminalCell cell = ref cells[x];
                bool tail = cell.Width == 0 && !cell.IsWideSpacerHead;
                uint value;
                if (tail)
                {
                    if (full) continue;
                    value = 0x1B;
                }
                else if (!cell.HasContent)
                {
                    if (!countUndrawn) continue;
                    value = ' ';
                }
                else
                {
                    int cp = cell.Codepoint;
                    value = full ? (ushort)cp : cp switch
                    {
                        0x7F or 0xFF => 0,
                        >= 0x20 and <= 0x7E or >= 0x80 and <= 0x9F or >= 0xA1 and <= 0xFE => (uint)(cp & 0x7F),
                        _ => 0x1B,
                    };
                }
                CellAttributes attributes = cell.Attributes;
                if ((_checksumFlags & TerminalChecksumFlags.NoAttributes) == 0)
                {
                    if (cell.IsProtected) value += 4;
                    if ((attributes & CellAttributes.Hidden) != 0) value += 8;
                    if (cell.UnderlineStyle != TerminalUnderlineStyle.None) value += 16;
                    if ((attributes & CellAttributes.Inverse) != 0) value += 32;
                    if ((attributes & CellAttributes.Blink) != 0) value += 64;
                    if ((attributes & CellAttributes.Bold) != 0) value += 128;
                }
                if (noTrim)
                {
                    sum += value;
                    if (!full && cell.Grapheme is { } text)
                    {
                        bool initial = true;
                        foreach (Rune rune in text.EnumerateRunes())
                        {
                            if (initial) { initial = false; continue; }
                            sum += (ushort)rune.Value;
                        }
                    }
                }
                else if (first || value != ' ' ||
                    (attributes & (CellAttributes.Dim | CellAttributes.Italic | CellAttributes.Strikethrough)) != 0 ||
                    cell.UnderlineStyle == TerminalUnderlineStyle.Double) sum += value;
                first = noTrim;
            }
            if (!noTrim) first = false;
        }
        return unchecked((ushort)((_checksumFlags & TerminalChecksumFlags.Positive) != 0 ? sum : 0 - sum));
    }
}
