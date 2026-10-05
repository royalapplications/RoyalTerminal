// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty src/crc32c.zig (36d8e3f77, eb09bf829).
// Copyright (c) 2024 Mitchell Hashimoto, Ghostty contributors.
// See THIRD-PARTY-NOTICES.txt for the upstream MIT notice.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Portable CRC32C update without applying the initial or final XOR.</summary>
internal static class GhosttySnapshotSoftwareCrc32C
{
    private const uint Polynomial = 0x82F63B78;
    private const int BlockBytes = 16;
    private const int InterleaveThreshold = 4096;

    internal static uint Append(uint initial, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return initial;
        ReadOnlySpan<uint> tables = SlicingTables.Values;
        if (bytes.Length < InterleaveThreshold) return AppendSingle(initial, bytes, tables);

        // The first two streams end on block boundaries. The third owns the
        // remaining bytes, so every fold in the interleaved loop is complete.
        int part = (bytes.Length / 3) & ~(BlockBytes - 1);
        ReadOnlySpan<byte> second = bytes.Slice(part, part), third = bytes[(2 * part)..];
        uint firstCrc = initial, secondCrc = 0, thirdCrc = 0;
        for (int i = 0; i < part; i += BlockBytes)
        {
            firstCrc = Fold(bytes[i..], firstCrc, tables);
            secondCrc = Fold(second[i..], secondCrc, tables);
            thirdCrc = Fold(third[i..], thirdCrc, tables);
        }
        thirdCrc = AppendSingle(thirdCrc, third[part..], tables);
        uint firstTwo = secondCrc ^ AdvanceZeros(firstCrc, part);
        return thirdCrc ^ AdvanceZeros(firstTwo, third.Length);
    }

    private static uint AppendSingle(uint crc, ReadOnlySpan<byte> bytes, ReadOnlySpan<uint> tables)
    {
        int offset = 0;
        for (; offset <= bytes.Length - BlockBytes; offset += BlockBytes)
            crc = Fold(bytes[offset..], crc, tables);
        for (; offset < bytes.Length; offset++)
            crc = (crc >> 8) ^ tables[(int)((crc ^ bytes[offset]) & 255)];
        return crc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Fold(ReadOnlySpan<byte> bytes, uint crc, ReadOnlySpan<uint> tables)
    {
        uint a = BinaryPrimitives.ReadUInt32LittleEndian(bytes) ^ crc;
        uint b = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        uint c = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        uint d = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        return FoldWord(a, 12 * 256, tables) ^ FoldWord(b, 8 * 256, tables) ^
            FoldWord(c, 4 * 256, tables) ^ FoldWord(d, 0, tables);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FoldWord(uint value, int offset, ReadOnlySpan<uint> tables)
        => tables[offset + 3 * 256 + (int)(value & 255)] ^
           tables[offset + 2 * 256 + (int)((value >> 8) & 255)] ^
           tables[offset + 256 + (int)((value >> 16) & 255)] ^
           tables[offset + (int)(value >> 24)];

    /// <summary>Advances an unfinalized CRC by a nonnegative number of zero bytes.</summary>
    internal static uint AdvanceZeros(uint crc, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0) return crc;
        ReadOnlySpan<uint> matrices = ShiftMatrices.Values;
        for (int power = 0; length != 0; length >>= 1, power++)
        {
            if ((length & 1) == 0) continue;
            ReadOnlySpan<uint> matrix = matrices.Slice(power / 2 * 32, 32);
            crc = Multiply(matrix, crc);
            if ((power & 1) != 0) crc = Multiply(matrix, crc);
        }
        return crc;
    }

    private static uint Multiply(ReadOnlySpan<uint> matrix, uint value)
    {
        uint result = 0;
        for (int bit = 0; value != 0; value >>= 1, bit++)
            if ((value & 1) != 0) result ^= matrix[bit];
        return result;
    }

    // Constant polynomial data is privately owned and never changed after
    // construction. Separate holders avoid initializing these tables on the
    // hardware path, or the shift matrices for small software-only records.
    private static class SlicingTables
    {
        private static readonly uint[] Data = Create();
        internal static ReadOnlySpan<uint> Values => Data;

        private static uint[] Create()
        {
            uint[] result = new uint[BlockBytes * 256];
            for (int value = 0; value < 256; value++)
            {
                uint crc = (uint)value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? Polynomial : 0);
                result[value] = crc;
            }
            for (int slice = 1; slice < BlockBytes; slice++)
            for (int value = 0; value < 256; value++)
            {
                uint previous = result[(slice - 1) * 256 + value];
                result[slice * 256 + value] = (previous >> 8) ^ result[(int)(previous & 255)];
            }
            return result;
        }
    }

    private static class ShiftMatrices
    {
        private static readonly uint[] Data = Create();
        internal static ReadOnlySpan<uint> Values => Data;

        private static uint[] Create()
        {
            // Spans are int-sized: sixteen even powers cover every possible
            // byte count. Odd powers apply the preceding matrix twice.
            uint[] result = new uint[16 * 32];
            ReadOnlySpan<uint> table = SlicingTables.Values;
            Span<uint> previous = stackalloc uint[32], next = stackalloc uint[32];
            for (int bit = 0; bit < 32; bit++)
            {
                uint unit = 1U << bit;
                previous[bit] = (unit >> 8) ^ table[(int)(unit & 255)];
            }
            previous.CopyTo(result);
            for (int power = 1; power <= 30; power++)
            {
                for (int bit = 0; bit < 32; bit++) next[bit] = Multiply(previous, previous[bit]);
                next.CopyTo(previous);
                if ((power & 1) == 0) previous.CopyTo(result.AsSpan(power / 2 * 32, 32));
            }
            return result;
        }
    }
}
