// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>A borrowed, byte-preserving hyperlink entry within a PAGE payload.</summary>
internal readonly ref struct GhosttySnapshotHyperlink(
    bool explicitId,
    uint implicitId,
    ReadOnlySpan<byte> id,
    ReadOnlySpan<byte> uri)
{
    internal bool HasExplicitId { get; } = explicitId;
    internal uint ImplicitId { get; } = implicitId;
    internal ReadOnlySpan<byte> ExplicitId { get; } = id;
    internal ReadOnlySpan<byte> Uri { get; } = uri;
    internal bool IsValid => !Uri.IsEmpty && (!HasExplicitId || !ExplicitId.IsEmpty);

    internal static GhosttySnapshotHyperlink Read(ReadOnlySpan<byte> bytes, out int consumed)
    {
        // Unknown kinds have no recoverable entry boundary. Empty strings are
        // semantic errors, but their lengths still let PAGE consume the entry.
        if (bytes.Length < 5) throw new EndOfStreamException();
        byte kind = bytes[0];
        if (kind != 1 && kind != 2) throw new InvalidDataException("Invalid Ghostty snapshot hyperlink kind.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[1..]);
        int offset = 5;
        ReadOnlySpan<byte> explicitId = default;
        if (kind == 2)
        {
            if (value > (uint)(bytes.Length - offset)) throw new EndOfStreamException();
            explicitId = bytes.Slice(offset, (int)value);
            offset += (int)value;
        }
        if (bytes.Length - offset < sizeof(uint)) throw new EndOfStreamException();
        uint uriLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        offset += sizeof(uint);
        if (uriLength > (uint)(bytes.Length - offset)) throw new EndOfStreamException();
        ReadOnlySpan<byte> uri = bytes.Slice(offset, (int)uriLength);
        consumed = offset + (int)uriLength;
        return new(kind == 2, kind == 1 ? value : 0, explicitId, uri);
    }

    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!IsValid) throw new InvalidDataException("Snapshot hyperlink strings must be non-empty.");
        Span<byte> prefix = stackalloc byte[5];
        prefix[0] = HasExplicitId ? (byte)2 : (byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[1..], HasExplicitId ? (uint)ExplicitId.Length : ImplicitId);
        destination.Write(prefix);
        if (HasExplicitId) destination.Write(ExplicitId);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)Uri.Length);
        destination.Write(prefix[..4]);
        destination.Write(Uri);
    }
}
