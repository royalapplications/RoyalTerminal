// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Filters wire suffix scalars before allocating their exact owned storage.</summary>
internal static class GhosttySnapshotSuffixCodec
{
    internal static uint[] Read(ReadOnlySpan<byte> encoded, int maximumCodepoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCodepoints);
        if (encoded.Length % 4 != 0) throw new EndOfStreamException();
        int length = encoded.Length / 4;
        int valid = CountValid(encoded);
        if (valid == 0) return [];
        if (valid > maximumCodepoints)
            throw new InvalidDataException("Snapshot graphemes exceed the configured codepoint limit.");
        uint[] result = new uint[valid];
        if (valid == length && BitConverter.IsLittleEndian)
        {
            // The common all-valid case needs no scalar second pass. The copy
            // still creates owned storage; callers cannot mutate borrowed input.
            MemoryMarshal.Cast<byte, uint>(encoded).CopyTo(result);
            return result;
        }
        int next = 0;
        for (int i = 0; i < encoded.Length; i += 4)
        {
            uint cp = BinaryPrimitives.ReadUInt32LittleEndian(encoded[i..]);
            if (IsValid(cp)) result[next++] = cp;
        }
        return result;
    }

    private static int CountValid(ReadOnlySpan<byte> encoded)
    {
        int i = 0, valid = 0;
        if (BitConverter.IsLittleEndian && Vector.IsHardwareAccelerated && encoded.Length / 4 >= Vector<uint>.Count)
        {
            ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(encoded);
            Vector<uint> counts = Vector<uint>.Zero;
            Vector<uint> scalarEnd = new(0x110000), surrogateMask = new(0xFFFFF800), surrogate = new(0xD800), one = new(1);
            for (; i <= words.Length - Vector<uint>.Count; i += Vector<uint>.Count)
            {
                Vector<uint> cp = new(words[i..]);
                Vector<uint> accepted = Vector.GreaterThan(cp, Vector<uint>.Zero) & Vector.LessThan(cp, scalarEnd) &
                    ~Vector.Equals(cp & surrogateMask, surrogate);
                counts += accepted & one;
            }
            valid = (int)Vector.Sum(counts);
            i *= 4;
        }
        for (; i < encoded.Length; i += 4)
            if (IsValid(BinaryPrimitives.ReadUInt32LittleEndian(encoded[i..]))) valid++;
        return valid;
    }

    private static bool IsValid(uint cp) => cp is > 0 and <= 0x10FFFF && cp is not (>= 0xD800 and <= 0xDFFF);
}
