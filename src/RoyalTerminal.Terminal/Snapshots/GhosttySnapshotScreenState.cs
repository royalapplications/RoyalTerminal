// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Complete owned SCREEN payload. Page counts route subsequent records; advisory
/// history extents never drive allocation. Coordinates are clamped at installation.
/// </summary>
internal sealed class GhosttySnapshotScreenState
{
    internal const int HeaderLength = 53;
    private readonly byte[] _header;
    private readonly byte[] _hyperlink;

    private GhosttySnapshotScreenState(byte[] header, GhosttySnapshotSavedCursor? savedCursor, byte[] hyperlink)
    { _header = header; SavedCursor = savedCursor; _hyperlink = hyperlink; }

    internal int Key => BinaryPrimitives.ReadUInt16LittleEndian(_header);
    internal int PageCount => BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(2));
    internal ulong HistoryRows => BinaryPrimitives.ReadUInt64LittleEndian(_header.AsSpan(4));
    internal int CursorX => BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(12));
    internal int CursorY => BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(14));
    internal byte CursorStyle => _header[16];
    internal bool PendingWrap => (_header[17] & 1) != 0;
    internal bool Protected => (_header[17] & 2) != 0;
    internal int SemanticContent => (_header[17] >> 2) & 3;
    internal bool SemanticContentClearEol => (_header[17] & 16) != 0;
    internal GhosttySnapshotStyle Pen => GhosttySnapshotStyle.Read(_header.AsSpan(18))!.Value;
    internal uint HyperlinkImplicitCounter => BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(34));
    internal GhosttySnapshotCharset Charset => GhosttySnapshotCharset.Read(BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(38)));
    internal byte ProtectedMode => _header[40];
    internal int KittyKeyboardIndex => _header[41];
    internal ReadOnlySpan<byte> KittyKeyboardFlags => _header.AsSpan(42, 8);
    internal byte SemanticClickKind => _header[50];
    internal byte SemanticClickValue => _header[51];
    internal GhosttySnapshotSavedCursor? SavedCursor { get; }

    internal bool TryGetHyperlink(out GhosttySnapshotHyperlink hyperlink)
    {
        if (_hyperlink.Length != 0)
        {
            hyperlink = GhosttySnapshotHyperlink.Read(_hyperlink, out _);
            return true;
        }
        hyperlink = default;
        return false;
    }

    /// <summary>The caller locates the clamped cursor row before supplying its physical page width.</summary>
    internal (int X, int Y, bool PendingWrap) GetCursorPosition(int columnsInCursorRow, int terminalRows)
    {
        GhosttySnapshotSavedCursor.ValidateExtent(columnsInCursorRow, nameof(columnsInCursorRow));
        GhosttySnapshotSavedCursor.ValidateExtent(terminalRows, nameof(terminalRows));
        int x = Math.Min(CursorX, columnsInCursorRow - 1);
        return (x, Math.Min(CursorY, terminalRows - 1), PendingWrap && x == columnsInCursorRow - 1);
    }

    internal static GhosttySnapshotScreenState Read(ReadOnlySpan<byte> payload, int maximumPages, int maximumStringBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPages);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStringBytes);
        if (payload.Length < HeaderLength) throw new EndOfStreamException();
        int key = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        int pages = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        if (key > 1) throw new InvalidDataException("Invalid snapshot screen key.");
        if (pages == 0 || pages > maximumPages) throw new InvalidDataException("Snapshot screen page count exceeds the configured limit.");
        bool hasSavedCursor = payload[52] != 0; // Every nonzero value declares a suffix, unlike optional booleans.
        GhosttySnapshotSavedCursor? savedCursor = hasSavedCursor
            ? GhosttySnapshotSavedCursor.Read(payload[HeaderLength..]) : null;
        int linkOffset = HeaderLength + (hasSavedCursor ? GhosttySnapshotSavedCursor.Length : 0);
        ReadOnlySpan<byte> linkBytes = payload[linkOffset..];
        if (linkBytes.IsEmpty) throw new EndOfStreamException();
        ReadOnlySpan<byte> acceptedLink = ReadCursorHyperlink(linkBytes, maximumStringBytes);

        byte[] header = payload[..HeaderLength].ToArray();
        if (header[16] > 3) header[16] = 1; // Unknown visual style becomes block.
        header[17] &= 0x1F;
        if ((header[17] & 12) == 12) header[17] &= 0xF3;
        (GhosttySnapshotStyle.Read(header.AsSpan(18)) ?? default).Write(header.AsSpan(18));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(38),
            GhosttySnapshotCharset.Read(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(38))).Bits);
        if (header[40] > 2) header[40] = 0;
        if (header[41] >= 8) header[41] = 0;
        for (int index = 42; index < 50; index++) header[index] &= 0x1F;
        if (header[50] == 0 || header[50] > 2 ||
            (header[50] == 1 && header[51] > 1) || (header[50] == 2 && header[51] > 3))
            header[50] = header[51] = 0;
        header[52] = hasSavedCursor ? (byte)1 : (byte)0;
        return new(header, savedCursor, acceptedLink.ToArray());
    }

    internal void WritePayloadTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Snapshot destination is not writable.", nameof(destination));
        destination.Write(_header);
        if (SavedCursor is { } saved)
        {
            Span<byte> buffer = stackalloc byte[GhosttySnapshotSavedCursor.Length];
            saved.Write(buffer);
            destination.Write(buffer);
        }
        if (_hyperlink.Length == 0) destination.WriteByte(0);
        else destination.Write(_hyperlink);
    }

    private static ReadOnlySpan<byte> ReadCursorHyperlink(ReadOnlySpan<byte> bytes, int maximumStringBytes)
    {
        if (bytes[0] == 0)
        {
            if (bytes.Length != 1) throw new InvalidDataException("Snapshot SCREEN has trailing payload bytes.");
            return default;
        }
        GhosttySnapshotHyperlink link;
        int consumed;
        try { link = GhosttySnapshotHyperlink.Read(bytes, out consumed); }
        catch (EndOfStreamException) { return default; }
        catch (InvalidDataException) { return default; }
        // Upstream discards the entire final field for malformed kinds, lengths
        // or empty values. A valid link, however, must exhaust the record.
        if (!link.IsValid) return default;
        if (consumed != bytes.Length) throw new InvalidDataException("Snapshot SCREEN has trailing payload bytes.");
        if ((long)link.Uri.Length + link.ExplicitId.Length > maximumStringBytes)
            throw new InvalidDataException("Snapshot cursor hyperlink exceeds the configured byte limit.");
        return bytes;
    }
}
