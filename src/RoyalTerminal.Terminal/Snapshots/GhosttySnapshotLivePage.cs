// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Converts validated PAGE data to/from the live cell model without terminal input
/// replay. Rows retain their physical width; installation must not resize them.
/// The hyperlink owner must be an unpublished staging screen when decoding.
/// </summary>
internal static class GhosttySnapshotLivePage
{
    internal static TerminalRow[] Decode(GhosttySnapshotPage page, TerminalScreen hyperlinkOwner)
    {
        GhosttySnapshotGrid grid = page.Grid;
        TerminalTheme theme = hyperlinkOwner.Theme;
        TerminalRow[] rows = new TerminalRow[grid.Rows];
        GhosttySnapshotPageAllocation allocation = new(page.Capacity);
        Dictionary<ushort, TerminalCell> styles = new(page.StyleCount);
        Dictionary<ushort, int> links = new(page.HyperlinkCount);
        Span<char> textScratch = stackalloc char[128];
        for (int rowIndex = 0; rowIndex < grid.Rows; rowIndex++)
        {
            byte flags = grid.RowFlags[rowIndex];
            TerminalRow row = new(grid.Columns, theme.DefaultForeground, theme.DefaultBackground)
            {
                WrapsToNext = (flags & 1) != 0,
                IsWrapContinuation = (flags & 2) != 0,
                SemanticPrompt = (TerminalSemanticPrompt)((flags >> 2) & 3),
            };
            for (int column = 0; column < grid.Columns; column++)
            {
                ulong bits = grid.Cells[rowIndex * grid.Columns + column];
                ushort styleId = (ushort)(bits >> 26);
                if (!styles.TryGetValue(styleId, out TerminalCell cell))
                {
                    page.TryGetStyle(styleId, out GhosttySnapshotStyle style);
                    cell = DecodeStyle(style, theme);
                    styles.Add(styleId, cell);
                }

                uint content = (uint)((bits >> 2) & 0xFFFFFF);
                int kind = (int)(bits & 3);
                if (kind <= 1)
                {
                    cell.Codepoint = (int)content;
                    ReadOnlySpan<uint> suffix = grid.Suffix(rowIndex, column);
                    if (!suffix.IsEmpty) cell.Grapheme = DecodeGrapheme(content, suffix, textScratch);
                }
                else
                {
                    // The inline background takes precedence over a style's
                    // background, as Ghostty Style.bg does, including black.
                    cell.BackgroundIdentity = kind == 2 ? TerminalColorIdentity.Palette((byte)content)
                        : TerminalColorIdentity.Rgb(((content & 255) << 16) | (content & 0xFF00) | (content >> 16));
                    cell.Background = Resolve(cell.BackgroundIdentity, theme.DefaultBackground, theme);
                    cell.HasBackground = true;
                }
                int width = (int)((bits >> 42) & 3);
                cell.Width = width switch { 1 => 2, 2 or 3 => 0, _ => 1 };
                cell.IsWideSpacerHead = width == 3;
                cell.IsProtected = (bits & (1UL << 44)) != 0;
                cell.SemanticContent = (TerminalSemanticContent)((bits >> 46) & 3);
                ushort linkId = (ushort)(bits >> 48);
                if (linkId != 0)
                {
                    if (!links.TryGetValue(linkId, out int token))
                    {
                        if (page.TryGetHyperlink(linkId, out GhosttySnapshotHyperlink link))
                            token = hyperlinkOwner.RegisterHyperlink(link.Uri, link.ExplicitId, link.ImplicitId);
                        links.Add(linkId, token);
                    }
                    cell.HyperlinkId = token;
                }
                row[column] = cell;
            }
            row.SnapshotAllocation = allocation;
            row.SnapshotAllocationRow = rowIndex;
            row.SnapshotAllocationUnmodified = true;
            rows[rowIndex] = row;
        }
        return rows;
    }

    internal static GhosttySnapshotPage Capture(ReadOnlySpan<TerminalRow> rows,
        TerminalScreen hyperlinkOwner, int maximumCells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCells);
        if (rows.IsEmpty || rows.Length > ushort.MaxValue || rows[0].Columns is < 1 or > ushort.MaxValue)
            throw new InvalidDataException("Invalid live snapshot PAGE dimensions.");
        int columns = rows[0].Columns;
        if ((long)columns * rows.Length > maximumCells)
            throw new InvalidDataException("Live snapshot PAGE exceeds its cell budget.");
        // Validate row dimensions before allocating or registering anything.
        foreach (TerminalRow row in rows)
            if (row.Columns != columns) throw new InvalidDataException("One snapshot PAGE cannot mix physical row widths.");
        byte[] rowFlags = new byte[rows.Length];
        ulong[] cells = new ulong[rows.Length * columns];
        Dictionary<int, uint[]> suffixes = [];
        Dictionary<GhosttySnapshotStyle, ushort> styleIds = [];
        Dictionary<ushort, GhosttySnapshotStyle> styles = [];
        Dictionary<int, ushort> linkIds = [];
        Dictionary<ushort, byte[]> links = [];
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            TerminalRow row = rows[rowIndex];
            if ((uint)row.SemanticPrompt > 2) throw new InvalidDataException("Invalid live row semantic prompt.");
            rowFlags[rowIndex] = (byte)((row.WrapsToNext ? 1 : 0) | (row.IsWrapContinuation ? 2 : 0) | ((int)row.SemanticPrompt << 2));
            for (int column = 0; column < columns; column++)
            {
                ref readonly TerminalCell cell = ref row.ReadOnlyCells[column];
                ValidateCell(row, column, in cell);
                GhosttySnapshotStyle style = EncodeStyle(in cell);
                ushort styleId = 0;
                if (style != default && !styleIds.TryGetValue(style, out styleId))
                {
                    styleId = NextId(styles.Count);
                    styles.Add(styleId, style);
                    styleIds.Add(style, styleId);
                }
                ushort linkId = 0;
                if (cell.HyperlinkId != 0 && !linkIds.TryGetValue(cell.HyperlinkId, out linkId))
                {
                    linkId = NextId(links.Count);
                    links.Add(linkId, EncodeLink(hyperlinkOwner, cell.HyperlinkId));
                    linkIds.Add(cell.HyperlinkId, linkId);
                }
                int index = rowIndex * columns + column;
                ulong bits = ((ulong)cell.Codepoint << 2) | ((ulong)styleId << 26) |
                    ((cell.IsWideSpacerHead ? 3UL : cell.Width switch { 2 => 1UL, 0 => 2UL, _ => 0UL }) << 42) |
                    (cell.IsProtected ? 1UL << 44 : 0) | (linkId == 0 ? 0 : 1UL << 45) |
                    ((ulong)cell.SemanticContent << 46) | ((ulong)linkId << 48);
                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    uint[] suffix = EncodeSuffix(cell.Codepoint, cell.Grapheme);
                    if (suffix.Length > 0)
                    {
                        suffixes.Add(index, suffix);
                        bits |= 1;
                    }
                }
                cells[index] = bits;
            }
        }
        return GhosttySnapshotPage.FromOwnedGrid(
            GhosttySnapshotGrid.FromOwnedCells(columns, rowFlags, cells, suffixes), styles, links);
    }

    internal static TerminalCell DecodeStyle(GhosttySnapshotStyle style, TerminalTheme theme)
    {
        TerminalCell cell = TerminalCell.Empty(theme.DefaultForeground, theme.DefaultBackground);
        cell.ForegroundIdentity = DecodeColor(style.Foreground);
        cell.BackgroundIdentity = DecodeColor(style.Background);
        cell.UnderlineIdentity = DecodeColor(style.UnderlineColor);
        cell.Foreground = Resolve(cell.ForegroundIdentity, theme.DefaultForeground, theme);
        cell.Background = Resolve(cell.BackgroundIdentity, theme.DefaultBackground, theme);
        cell.HasBackground = cell.BackgroundIdentity.Kind != TerminalColorKind.Default;
        cell.HasUnderlineColor = cell.UnderlineIdentity.Kind != TerminalColorKind.Default;
        cell.UnderlineColor = cell.HasUnderlineColor ? Resolve(cell.UnderlineIdentity, cell.Foreground, theme) : 0;
        ushort flags = style.Flags;
        cell.Attributes = ((flags & 1) != 0 ? CellAttributes.Bold : 0) |
            ((flags & 2) != 0 ? CellAttributes.Italic : 0) |
            ((flags & 4) != 0 ? CellAttributes.Dim : 0) |
            ((flags & 8) != 0 ? CellAttributes.Blink : 0) |
            ((flags & 16) != 0 ? CellAttributes.Inverse : 0) |
            ((flags & 32) != 0 ? CellAttributes.Hidden : 0) |
            ((flags & 64) != 0 ? CellAttributes.Strikethrough : 0);
        cell.Decorations = (flags & 128) != 0 ? CellDecorations.Overline : 0;
        cell.UnderlineStyle = (TerminalUnderlineStyle)((flags >> 8) & 7);
        if (cell.UnderlineStyle != TerminalUnderlineStyle.None) cell.Attributes |= CellAttributes.Underline;
        return cell;
    }

    internal static GhosttySnapshotStyle EncodeStyle(in TerminalCell cell)
    {
        CellAttributes a = cell.Attributes;
        int underline = (int)cell.UnderlineStyle;
        if (underline == 0 && (a & CellAttributes.Underline) != 0) underline = 1;
        ushort flags = (ushort)(((a & CellAttributes.Bold) != 0 ? 1 : 0) |
            ((a & CellAttributes.Italic) != 0 ? 2 : 0) | ((a & CellAttributes.Dim) != 0 ? 4 : 0) |
            ((a & CellAttributes.Blink) != 0 ? 8 : 0) | ((a & CellAttributes.Inverse) != 0 ? 16 : 0) |
            ((a & CellAttributes.Hidden) != 0 ? 32 : 0) | ((a & CellAttributes.Strikethrough) != 0 ? 64 : 0) |
            ((cell.Decorations & CellDecorations.Overline) != 0 ? 128 : 0) | (underline << 8));
        return new(EncodeColor(cell.ForegroundIdentity), EncodeColor(cell.BackgroundIdentity),
            cell.HasUnderlineColor ? EncodeColor(cell.UnderlineIdentity) : default, flags);
    }

    private static TerminalColorIdentity DecodeColor(GhosttySnapshotColor color) => color.Kind switch
    {
        1 => TerminalColorIdentity.Palette(color.First),
        2 => TerminalColorIdentity.Rgb((uint)(color.First << 16 | color.Second << 8 | color.Third)),
        _ => default,
    };

    private static GhosttySnapshotColor EncodeColor(TerminalColorIdentity color) => color.Kind switch
    {
        TerminalColorKind.Palette => new(1, (byte)color.Value, 0, 0),
        TerminalColorKind.Rgb => new(2, (byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value),
        _ => default,
    };

    private static uint Resolve(TerminalColorIdentity color, uint fallback, TerminalTheme theme) => color.Kind switch
    {
        TerminalColorKind.Palette => theme.Palette[(int)color.Value],
        TerminalColorKind.Rgb => 0xFF000000 | color.Value,
        _ => fallback,
    };

    private static string DecodeGrapheme(uint primary, ReadOnlySpan<uint> suffix, Span<char> scratch)
    {
        int length = new Rune((int)primary).Utf16SequenceLength;
        foreach (uint scalar in suffix) length += new Rune((int)scalar).Utf16SequenceLength;
        char[]? rented = length > scratch.Length ? ArrayPool<char>.Shared.Rent(length) : null;
        Span<char> destination = rented is null ? scratch : rented;
        try
        {
            int written = new Rune((int)primary).EncodeToUtf16(destination);
            foreach (uint scalar in suffix) written += new Rune((int)scalar).EncodeToUtf16(destination[written..]);
            return new string(destination[..written]);
        }
        finally { if (rented is not null) ArrayPool<char>.Shared.Return(rented); }
    }

    private static uint[] EncodeSuffix(int primary, string grapheme)
    {
        ReadOnlySpan<char> text = grapheme;
        if (Rune.DecodeFromUtf16(text, out Rune first, out int consumed) != OperationStatus.Done || first.Value != primary)
            throw new InvalidDataException("A live grapheme must begin with its cell codepoint.");
        text = text[consumed..];
        int count = 0;
        for (ReadOnlySpan<char> remaining = text; !remaining.IsEmpty;)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune rune, out consumed) != OperationStatus.Done || rune.Value == 0)
                throw new InvalidDataException("Invalid live grapheme suffix.");
            remaining = remaining[consumed..];
            if (++count > ushort.MaxValue) throw new InvalidDataException("Snapshot grapheme suffix exceeds the wire limit.");
        }
        uint[] result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            Rune.DecodeFromUtf16(text, out Rune rune, out consumed);
            result[i] = (uint)rune.Value;
            text = text[consumed..];
        }
        return result;
    }

    private static byte[] EncodeLink(TerminalScreen owner, int token)
    {
        if (owner.TryGetHyperlink(token, out TerminalHyperlink? link) && link is not null)
        {
            ReadOnlySpan<byte> id = link.ExplicitId;
            byte[] bytes = new byte[checked(9 + id.Length + link.UriBytes.Length)];
            bytes[0] = id.IsEmpty ? (byte)1 : (byte)2;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), id.IsEmpty ? link.ImplicitId : (uint)id.Length);
            id.CopyTo(bytes.AsSpan(5));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(5 + id.Length), (uint)link.UriBytes.Length);
            link.UriBytes.CopyTo(bytes.AsSpan(9 + id.Length));
            return bytes;
        }
        // Compatibility with caller-created cells using the older URL registry.
        if (!owner.TryGetHyperlinkUrl(token, out string? url) || string.IsNullOrEmpty(url)) throw new InvalidDataException("Unresolved live hyperlink token.");
        int length = Encoding.UTF8.GetByteCount(url);
        byte[] legacy = new byte[checked(9 + length)];
        legacy[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(1), checked((uint)token));
        BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(5), (uint)length);
        Encoding.UTF8.GetBytes(url, legacy.AsSpan(9));
        return legacy;
    }

    private static ushort NextId(int count) => count < ushort.MaxValue ? (ushort)(count + 1)
        : throw new InvalidDataException("Split the snapshot PAGE before its local ID table overflows.");

    private static void ValidateCell(TerminalRow row, int column, in TerminalCell cell)
    {
        if (!Rune.IsValid(cell.Codepoint) || (uint)cell.SemanticContent > 2 || (uint)cell.UnderlineStyle > 5 ||
            (cell.Decorations & ~CellDecorations.Overline) != 0 ||
            cell.Codepoint == 0 && !string.IsNullOrEmpty(cell.Grapheme) ||
            cell.Width > 2 || cell.IsWideSpacerHead && (cell.Width != 0 || column != row.Columns - 1 || !row.WrapsToNext) ||
            cell.Width == 2 && (column + 1 >= row.Columns || row.ReadOnlyCells[column + 1].Width != 0) ||
            cell.Width == 0 && !cell.IsWideSpacerHead && (column == 0 || row.ReadOnlyCells[column - 1].Width != 2))
            throw new InvalidDataException("Invalid live snapshot cell.");
    }
}
