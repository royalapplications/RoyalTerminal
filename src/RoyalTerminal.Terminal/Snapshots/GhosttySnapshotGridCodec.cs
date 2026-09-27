// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Bounded row-word transport; no ownership, ID resolution or suffix allocation.</summary>
internal static class GhosttySnapshotGridCodec
{
    private const ulong ContentMask = 0xFFFFFFUL << 2;
    private const ulong WidthMask = 3UL << 42;
    private const ulong HyperlinkFlag = 1UL << 45;

    internal static (int Count, int Selector) Classify(ReadOnlySpan<ulong> cells)
    {
        int count = cells.LastIndexOfAnyExcept(0UL) + 1;
        ulong combined = 0;
        int i = 0;
        if (Vector.IsHardwareAccelerated && count >= Vector<ulong>.Count)
        {
            Vector<ulong> aggregate = Vector<ulong>.Zero;
            for (; i <= count - Vector<ulong>.Count; i += Vector<ulong>.Count)
                aggregate |= new Vector<ulong>(cells[i..]);
            for (int lane = 0; lane < Vector<ulong>.Count; lane++) combined |= aggregate[lane];
        }
        for (; i < count; i++) combined |= cells[i];
        int selector = (combined & ~(0xFFUL << 2)) == 0 ? 0 :
            (combined & ~(0xFFFFUL << 2)) == 0 ? 1 : combined <= uint.MaxValue ? 2 : 3;
        return (count, selector);
    }

    // Width was selected from all row bits, so every narrowing operation is
    // lossless. Validate both spans before writing even a partial vector/tail.
    internal static void Encode(ReadOnlySpan<ulong> cells, Span<byte> output, int width)
    {
        ValidateWidth(width);
        ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, checked(cells.Length * width));
        int i = 0;
        if (BitConverter.IsLittleEndian)
        {
            if (width == 8)
            {
                MemoryMarshal.AsBytes(cells).CopyTo(output);
                return;
            }
            if (Vector.IsHardwareAccelerated)
            {
                switch (width)
                {
                    case 1:
                        for (; i <= cells.Length - Vector<byte>.Count; i += Vector<byte>.Count)
                            Vector.Narrow(Narrow16(cells[i..]), Narrow16(cells[(i + Vector<ushort>.Count)..]))
                                .CopyTo(output[i..]);
                        break;
                    case 2:
                        Span<ushort> words16 = MemoryMarshal.Cast<byte, ushort>(output);
                        for (; i <= cells.Length - Vector<ushort>.Count; i += Vector<ushort>.Count)
                            Narrow16(cells[i..]).CopyTo(words16[i..]);
                        break;
                    case 4:
                        Span<uint> words32 = MemoryMarshal.Cast<byte, uint>(output);
                        for (; i <= cells.Length - Vector<uint>.Count; i += Vector<uint>.Count)
                            Vector.Narrow(new Vector<ulong>(cells[i..]), new Vector<ulong>(cells[(i + Vector<ulong>.Count)..]))
                                .CopyTo(words32[i..]);
                        break;
                }
            }
        }
        for (; i < cells.Length; i++)
        {
            ulong word = cells[i];
            Span<byte> target = output[(i * width)..];
            switch (width)
            {
                case 1: target[0] = (byte)(word >> 2); break;
                case 2: BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)(word >> 2)); break;
                case 4: BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)word); break;
                default: BinaryPrimitives.WriteUInt64LittleEndian(target, word); break;
            }
        }
    }

    // Returns whether the row needs the separate wide-pair repair pass. Narrow
    // transports cannot contain width flags; full words accumulate their flags
    // during normalization rather than requiring another scan of ordinary rows.
    internal static bool Decode(ReadOnlySpan<byte> input, Span<ulong> cells, int width)
    {
        ValidateWidth(width);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Length, checked(cells.Length * width));
        int i = 0;
        if (BitConverter.IsLittleEndian && Vector.IsHardwareAccelerated)
        {
            if (width == 1)
            {
                for (; i <= cells.Length - Vector<byte>.Count; i += Vector<byte>.Count)
                {
                    Vector.Widen(new Vector<byte>(input[i..]), out Vector<ushort> lo, out Vector<ushort> hi);
                    Widen16(lo, cells[i..]);
                    Widen16(hi, cells[(i + Vector<ushort>.Count)..]);
                }
            }
            else if (width == 2)
            {
                ReadOnlySpan<ushort> words = MemoryMarshal.Cast<byte, ushort>(input);
                Vector<ushort> surrogateMask = new(0xF800), surrogate = new(0xD800), replacement = new(0xFFFD);
                for (; i <= cells.Length - Vector<ushort>.Count; i += Vector<ushort>.Count)
                {
                    Vector<ushort> value = new(words[i..]);
                    value = Vector.ConditionalSelect(Vector.Equals(value & surrogateMask, surrogate), replacement, value);
                    Widen16(value, cells[i..]);
                }
            }
        }
        ulong combined = 0;
        for (; i < cells.Length; i++)
        {
            ReadOnlySpan<byte> word = input[(i * width)..];
            ulong bits = width switch
            {
                1 => (ulong)word[0] << 2,
                2 => (ulong)BinaryPrimitives.ReadUInt16LittleEndian(word) << 2,
                4 => BinaryPrimitives.ReadUInt32LittleEndian(word),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(word),
            };
            cells[i] = NormalizeCell(bits);
            combined |= bits;
        }
        return (combined & WidthMask) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ushort> Narrow16(ReadOnlySpan<ulong> cells)
    {
        int n = Vector<ulong>.Count;
        return Vector.Narrow(
            Vector.Narrow(new Vector<ulong>(cells) >> 2, new Vector<ulong>(cells[n..]) >> 2),
            Vector.Narrow(new Vector<ulong>(cells[(n * 2)..]) >> 2, new Vector<ulong>(cells[(n * 3)..]) >> 2));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Widen16(Vector<ushort> value, Span<ulong> cells)
    {
        Vector.Widen(value, out Vector<uint> lo, out Vector<uint> hi);
        Widen32(lo, cells);
        Widen32(hi, cells[Vector<uint>.Count..]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Widen32(Vector<uint> value, Span<ulong> cells)
    {
        Vector.Widen(value, out Vector<ulong> lo, out Vector<ulong> hi);
        (lo << 2).CopyTo(cells);
        (hi << 2).CopyTo(cells[Vector<ulong>.Count..]);
    }

    private static ulong NormalizeCell(ulong cell)
    {
        uint content = (uint)((cell >> 2) & 0xFFFFFF);
        switch (cell & 3)
        {
            case 0:
            case 1:
                cell &= ~3UL;
                if (content > 0x10FFFF || content is >= 0xD800 and <= 0xDFFF)
                    cell = (cell & ~ContentMask) | (0xFFFDUL << 2);
                break;
            case 2: cell = (cell & ~ContentMask) | ((ulong)(byte)content << 2); break;
        }
        if (((cell >> 46) & 3) == 3) cell &= ~(3UL << 46);
        return (cell >> 48) != 0 ? cell | HyperlinkFlag : cell & ~HyperlinkFlag;
    }

    private static void ValidateWidth(int width)
    {
        if (width is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(width));
    }
}
