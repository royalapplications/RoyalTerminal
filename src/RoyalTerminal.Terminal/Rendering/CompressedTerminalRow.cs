// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Rendering;

// Owned, process-local payload, never an external file format. Row flags and
// snapshot allocation metadata stay on the row. Numeric fields are explicit:
// copying TerminalCell bytes would serialize managed references and padding.
internal sealed class CompressedTerminalRow(byte[] bytes, int columns, ulong logicalBytes)
{
    internal int Columns => columns;
    internal ulong LogicalBytes => logicalBytes;
    internal ulong StoredBytes => (ulong)bytes.Length;

    internal static CompressedTerminalRow? TryCreate(ReadOnlySpan<TerminalCell> cells)
    {
        ulong logical = Measure(cells);
        ulong serializedLength = logical - (ulong)cells.Length * (uint)Unsafe.SizeOf<TerminalCell>() + (ulong)cells.Length * 41;
        if (serializedLength == 0 || serializedLength > int.MaxValue) return null;
        using MemoryStream raw = new((int)serializedLength);
        using (BinaryWriter writer = new(raw, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (ref readonly TerminalCell cell in cells)
            {
                writer.Write(cell.Codepoint);
                writer.Write(cell.Foreground);
                writer.Write(cell.Background);
                WriteColor(writer, cell.ForegroundIdentity);
                WriteColor(writer, cell.BackgroundIdentity);
                WriteColor(writer, cell.UnderlineIdentity);
                writer.Write(cell.UnderlineColor);
                writer.Write(cell.HyperlinkId);
                writer.Write((byte)cell.Attributes);
                writer.Write((byte)cell.UnderlineStyle);
                writer.Write((byte)cell.Decorations);
                writer.Write(cell.Width);
                writer.Write((byte)((cell.HasUnderlineColor ? 1 : 0) | (cell.HasBackground ? 2 : 0) |
                    (cell.IsWideSpacerHead ? 4 : 0) | (cell.IsProtected ? 8 : 0) | ((byte)cell.SemanticContent << 4)));
                writer.Write(cell.Grapheme?.Length ?? -1);
                if (cell.Grapheme is { } grapheme)
                    writer.Write(MemoryMarshal.AsBytes(grapheme.AsSpan()));
            }
        }
        // Compress a complete row in one call. Tiny streaming writes at low
        // Brotli quality create independent blocks and can enlarge the payload.
        byte[] scratch = ArrayPool<byte>.Shared.Rent((int)raw.Length);
        try
        {
            return BrotliEncoder.TryCompress(raw.GetBuffer().AsSpan(0, (int)raw.Length), scratch, out int written,
                quality: 1, window: 22) && (ulong)written < logical
                ? new(scratch.AsSpan(0, written).ToArray(), cells.Length, logical) : null;
        }
        finally { ArrayPool<byte>.Shared.Return(scratch); }
    }

    internal TerminalCell[] Restore()
    {
        TerminalCell[] cells = new TerminalCell[columns];
        using MemoryStream encoded = new(bytes, writable: false);
        using BrotliStream decompressor = new(encoded, CompressionMode.Decompress);
        using BinaryReader reader = new(decompressor);
        for (int i = 0; i < cells.Length; i++)
        {
            ref TerminalCell cell = ref cells[i];
            cell.Codepoint = reader.ReadInt32();
            cell.Foreground = reader.ReadUInt32();
            cell.Background = reader.ReadUInt32();
            cell.ForegroundIdentity = ReadColor(reader);
            cell.BackgroundIdentity = ReadColor(reader);
            cell.UnderlineIdentity = ReadColor(reader);
            cell.UnderlineColor = reader.ReadUInt32();
            cell.HyperlinkId = reader.ReadInt32();
            cell.Attributes = (CellAttributes)reader.ReadByte();
            cell.UnderlineStyle = (TerminalUnderlineStyle)reader.ReadByte();
            cell.Decorations = (CellDecorations)reader.ReadByte();
            cell.Width = reader.ReadByte();
            byte flags = reader.ReadByte();
            cell.HasUnderlineColor = (flags & 1) != 0;
            cell.HasBackground = (flags & 2) != 0;
            cell.IsWideSpacerHead = (flags & 4) != 0;
            cell.IsProtected = (flags & 8) != 0;
            cell.SemanticContent = (TerminalSemanticContent)(flags >> 4);
            int length = reader.ReadInt32();
            if (length >= 0)
                cell.Grapheme = string.Create(length, decompressor,
                    static (text, stream) => stream.ReadExactly(MemoryMarshal.AsBytes(text)));
        }
        return cells;
    }

    internal static ulong Measure(ReadOnlySpan<TerminalCell> cells)
    {
        ulong bytes = (ulong)cells.Length * (uint)Unsafe.SizeOf<TerminalCell>();
        foreach (ref readonly TerminalCell cell in cells)
            bytes += (ulong)(cell.Grapheme?.Length ?? 0) * sizeof(char);
        return bytes;
    }

    private static void WriteColor(BinaryWriter writer, TerminalColorIdentity color)
        => writer.Write(((uint)color.Kind << 24) | color.Value);

    private static TerminalColorIdentity ReadColor(BinaryReader reader)
    {
        uint value = reader.ReadUInt32();
        return (TerminalColorKind)(value >> 24) switch
        {
            TerminalColorKind.Palette => TerminalColorIdentity.Palette((byte)value),
            TerminalColorKind.Rgb => TerminalColorIdentity.Rgb(value),
            _ => default,
        };
    }
}
