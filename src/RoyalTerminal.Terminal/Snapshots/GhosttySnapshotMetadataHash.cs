// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty style/hyperlink hashing and Zig 0.16 std/hash/wyhash.zig.
// See ../THIRD-PARTY-NOTICES.txt for the upstream notices.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

// Pinned Ghostty PackedStyle/std.hash.int and PageEntry/std.hash.Wyhash.
// Matching the hash is necessary: RefCountedSet's probe-length pressure changes
// which optional snapshot entries survive, even below the nominal load limit.
internal static class GhosttySnapshotMetadataHash
{
    internal static ulong Style(GhosttySnapshotStyle style)
    {
        UInt128 packed = (UInt128)style.Foreground.Kind | (UInt128)style.Background.Kind << 8 |
            (UInt128)style.UnderlineColor.Kind << 16 | Data(style.Foreground) << 24 |
            Data(style.Background) << 48 | Data(style.UnderlineColor) << 72 | (UInt128)style.Flags << 96;
        unchecked
        {
            ulong value = (ulong)packed ^ (ulong)(packed >> 64);
            const ulong multiplier = 0xbea225f9eb34556d;
            value = (value ^ (value >> 32)) * multiplier;
            value = (value ^ (value >> 29)) * multiplier;
            value = (value ^ (value >> 32)) * multiplier;
            return value ^ (value >> 29);
        }
    }

    private static UInt128 Data(GhosttySnapshotColor color)
        => (UInt128)(uint)(color.First | color.Second << 8 | color.Third << 16);

    internal static ulong Hyperlink(GhosttySnapshotHyperlink link)
    {
        Span<byte> scratch = stackalloc byte[48];
        GhosttySnapshotWyhash hash = new(scratch);
        Span<byte> scalar = stackalloc byte[8];
        scalar[0] = link.HasExplicitId ? (byte)0 : (byte)1; // PageEntry.Id tag, not wire Kind.
        hash.Update(scalar[..1]);
        if (link.HasExplicitId)
        {
            hash.Update(link.ExplicitId);
            BinaryPrimitives.WriteUInt64LittleEndian(scalar, (ulong)link.ExplicitId.Length);
            hash.Update(scalar);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(scalar, link.ImplicitId);
            hash.Update(scalar[..4]);
        }
        hash.Update(link.Uri);
        BinaryPrimitives.WriteUInt64LittleEndian(scalar, (ulong)link.Uri.Length);
        hash.Update(scalar);
        return hash.Finish();
    }
}

// Streaming Zig 0.16 Wyhash; caller-owned 48-byte scratch, no heap allocation.
internal ref struct GhosttySnapshotWyhash
{
    private const ulong Secret0 = 0xa0761d6478bd642f, Secret1 = 0xe7037ed1a0b428db;
    private const ulong Secret2 = 0x8ebc6af09c88c6e3, Secret3 = 0x589965cc75374cc3;
    private readonly Span<byte> _buffer;
    private ulong _state0, _state1, _state2;
    private int _total, _length;

    internal GhosttySnapshotWyhash(Span<byte> scratch, ulong seed = 0)
    {
        if (scratch.Length < 48) throw new ArgumentException("Wyhash requires 48 scratch bytes.", nameof(scratch));
        _buffer = scratch[..48];
        _state0 = _state1 = _state2 = seed ^ Mix(seed ^ Secret0, Secret1);
        _total = _length = 0;
    }

    internal void Update(scoped ReadOnlySpan<byte> input)
    {
        _total = checked(_total + input.Length);
        if (input.Length <= 48 - _length)
        {
            input.CopyTo(_buffer[_length..]);
            _length += input.Length;
            return;
        }
        int offset = 0;
        if (_length > 0)
        {
            offset = 48 - _length;
            input[..offset].CopyTo(_buffer[_length..]);
            Round(_buffer);
            _length = 0;
        }
        while (offset + 48 < input.Length)
        {
            Round(input.Slice(offset, 48));
            offset += 48;
        }
        ReadOnlySpan<byte> remaining = input[offset..];
        if (remaining.Length < 16 && offset >= 48)
        {
            int prefix = 16 - remaining.Length;
            input.Slice(offset - prefix, prefix).CopyTo(_buffer[(48 - prefix)..]);
        }
        remaining.CopyTo(_buffer);
        _length = remaining.Length;
    }

    internal readonly ulong Finish()
    {
        ulong state = _state0, a, b;
        ReadOnlySpan<byte> input = _buffer[.._length];
        if (_total <= 16)
        {
            if (_length >= 4)
            {
                int end = _length - 4, quarter = (_length >> 3) << 2;
                a = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(input) << 32 |
                    BinaryPrimitives.ReadUInt32LittleEndian(input[quarter..]);
                b = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(input[end..]) << 32 |
                    BinaryPrimitives.ReadUInt32LittleEndian(input[(end - quarter)..]);
            }
            else
            {
                a = _length == 0 ? 0 : (ulong)input[0] << 16 | (ulong)input[_length >> 1] << 8 | input[_length - 1];
                b = 0;
            }
        }
        else
        {
            state ^= _state1 ^ _state2;
            for (int i = 0; i + 16 < input.Length; i += 16)
                state = Mix(BinaryPrimitives.ReadUInt64LittleEndian(input[i..]) ^ Secret1,
                    BinaryPrimitives.ReadUInt64LittleEndian(input[(i + 8)..]) ^ state);
            Span<byte> tail = stackalloc byte[16];
            if (input.Length < 16)
            {
                _buffer[(48 - (16 - input.Length))..].CopyTo(tail);
                input.CopyTo(tail[(16 - input.Length)..]);
            }
            else input[^16..].CopyTo(tail);
            a = BinaryPrimitives.ReadUInt64LittleEndian(tail);
            b = BinaryPrimitives.ReadUInt64LittleEndian(tail[8..]);
        }
        UInt128 product = (UInt128)(a ^ Secret1) * (b ^ state);
        return Mix(unchecked((ulong)product) ^ Secret0 ^ (ulong)_total, (ulong)(product >> 64) ^ Secret1);
    }

    private void Round(scoped ReadOnlySpan<byte> block)
    {
        _state0 = Mix(BinaryPrimitives.ReadUInt64LittleEndian(block) ^ Secret1, BinaryPrimitives.ReadUInt64LittleEndian(block[8..]) ^ _state0);
        _state1 = Mix(BinaryPrimitives.ReadUInt64LittleEndian(block[16..]) ^ Secret2, BinaryPrimitives.ReadUInt64LittleEndian(block[24..]) ^ _state1);
        _state2 = Mix(BinaryPrimitives.ReadUInt64LittleEndian(block[32..]) ^ Secret3, BinaryPrimitives.ReadUInt64LittleEndian(block[40..]) ^ _state2);
    }

    private static ulong Mix(ulong a, ulong b)
    {
        UInt128 product = (UInt128)a * b;
        return unchecked((ulong)product) ^ (ulong)(product >> 64);
    }
}
