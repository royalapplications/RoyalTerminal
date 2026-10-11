// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Rendering;

// Owned, process-local payload, never an external file format. Row flags and
// snapshot allocation metadata stay on the row. Numeric fields are explicit:
// copying TerminalCell bytes would serialize managed references and padding.
internal sealed class CompressedTerminalRow(byte[] bytes, int columns, ulong logicalBytes, int decodedLength)
{
    private const int CellHeaderBytes = 41;
    internal int Columns => columns;
    internal ulong LogicalBytes => logicalBytes;
    internal ulong StoredBytes => (ulong)bytes.Length;

    internal static CompressedTerminalRow? TryCreate(ReadOnlySpan<TerminalCell> cells)
    {
        ulong logical = Measure(cells);
        ulong serializedLength = logical - (ulong)cells.Length * (uint)Unsafe.SizeOf<TerminalCell>() + (ulong)cells.Length * CellHeaderBytes;
        if (serializedLength == 0 || serializedLength > int.MaxValue) return null;
        byte[] raw = ArrayPool<byte>.Shared.Rent((int)serializedLength);
        try
        {
            Span<byte> serialized = raw.AsSpan(0, (int)serializedLength);
            WriteCells(cells, serialized);
            // One complete row per call preserves the compression ratio at
            // quality 1. Both temporary buffers are reusable across rows.
            byte[] scratch = ArrayPool<byte>.Shared.Rent(serialized.Length);
            try
            {
                return BrotliEncoder.TryCompress(serialized, scratch, out int written, quality: 1, window: 22) &&
                    (ulong)written < logical
                    ? new(scratch.AsSpan(0, written).ToArray(), cells.Length, logical, serialized.Length) : null;
            }
            finally { ArrayPool<byte>.Shared.Return(scratch); }
        }
        finally { ArrayPool<byte>.Shared.Return(raw); }
    }

    private static void WriteCells(ReadOnlySpan<TerminalCell> cells, Span<byte> destination)
    {
        foreach (ref readonly TerminalCell cell in cells)
        {
            Span<byte> header = destination[..CellHeaderBytes];
            BinaryPrimitives.WriteInt32LittleEndian(header, cell.Codepoint);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], cell.Foreground);
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], cell.Background);
            WriteColor(header[12..], cell.ForegroundIdentity);
            WriteColor(header[16..], cell.BackgroundIdentity);
            WriteColor(header[20..], cell.UnderlineIdentity);
            BinaryPrimitives.WriteUInt32LittleEndian(header[24..], cell.UnderlineColor);
            BinaryPrimitives.WriteInt32LittleEndian(header[28..], cell.HyperlinkId);
            header[32] = (byte)cell.Attributes;
            header[33] = (byte)cell.UnderlineStyle;
            header[34] = (byte)cell.Decorations;
            header[35] = cell.Width;
            header[36] = (byte)((cell.HasUnderlineColor ? 1 : 0) | (cell.HasBackground ? 2 : 0) |
                (cell.IsWideSpacerHead ? 4 : 0) | (cell.IsProtected ? 8 : 0) | ((byte)cell.SemanticContent << 4));
            BinaryPrimitives.WriteInt32LittleEndian(header[37..], cell.Grapheme?.Length ?? -1);
            destination = destination[CellHeaderBytes..];
            if (cell.Grapheme is { } grapheme)
            {
                ReadOnlySpan<byte> text = MemoryMarshal.AsBytes(grapheme.AsSpan());
                text.CopyTo(destination);
                destination = destination[text.Length..];
            }
        }
    }

    internal TerminalCell[] Restore()
    {
        // Decompress once, instead of reentering the Brotli stream for every
        // numeric field. Publication still waits for all cells to be restored.
        byte[] raw = ArrayPool<byte>.Shared.Rent(decodedLength);
        try
        {
            Span<byte> serialized = raw.AsSpan(0, decodedLength);
            if (!BrotliDecoder.TryDecompress(bytes, serialized, out int written) || written != decodedLength)
                throw new InvalidDataException("Invalid compressed terminal row.");
            return ReadCells(serialized);
        }
        finally { ArrayPool<byte>.Shared.Return(raw); }
    }

    private TerminalCell[] ReadCells(ReadOnlySpan<byte> source)
    {
        TerminalCell[] cells = new TerminalCell[columns];
        for (int i = 0; i < cells.Length; i++)
        {
            ReadOnlySpan<byte> header = source[..CellHeaderBytes];
            ref TerminalCell cell = ref cells[i];
            cell.Codepoint = BinaryPrimitives.ReadInt32LittleEndian(header);
            cell.Foreground = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            cell.Background = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            cell.ForegroundIdentity = ReadColor(header[12..]);
            cell.BackgroundIdentity = ReadColor(header[16..]);
            cell.UnderlineIdentity = ReadColor(header[20..]);
            cell.UnderlineColor = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            cell.HyperlinkId = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
            cell.Attributes = (CellAttributes)header[32];
            cell.UnderlineStyle = (TerminalUnderlineStyle)header[33];
            cell.Decorations = (CellDecorations)header[34];
            cell.Width = header[35];
            byte flags = header[36];
            cell.HasUnderlineColor = (flags & 1) != 0;
            cell.HasBackground = (flags & 2) != 0;
            cell.IsWideSpacerHead = (flags & 4) != 0;
            cell.IsProtected = (flags & 8) != 0;
            cell.SemanticContent = (TerminalSemanticContent)(flags >> 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header[37..]);
            source = source[CellHeaderBytes..];
            if (length >= 0)
            {
                int byteLength = checked(length * sizeof(char));
                // Raw UTF-16 preserves null vs empty and unpaired surrogates.
                cell.Grapheme = new string(MemoryMarshal.Cast<byte, char>(source[..byteLength]));
                source = source[byteLength..];
            }
        }
        if (!source.IsEmpty) throw new InvalidDataException("Unexpected terminal row payload.");
        return cells;
    }

    internal static ulong Measure(ReadOnlySpan<TerminalCell> cells)
    {
        ulong bytes = (ulong)cells.Length * (uint)Unsafe.SizeOf<TerminalCell>();
        foreach (ref readonly TerminalCell cell in cells)
            bytes += (ulong)(cell.Grapheme?.Length ?? 0) * sizeof(char);
        return bytes;
    }

    private static void WriteColor(Span<byte> destination, TerminalColorIdentity color)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination, ((uint)color.Kind << 24) | color.Value);

    private static TerminalColorIdentity ReadColor(ReadOnlySpan<byte> source)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        return (TerminalColorKind)(value >> 24) switch
        {
            TerminalColorKind.Palette => TerminalColorIdentity.Palette((byte)value),
            TerminalColorKind.Rgb => TerminalColorIdentity.Rgb(value),
            _ => default,
        };
    }
}
