// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.Terminal;

// Ghostty Tabstops: 512 inline columns, then one bit per additional column.
// Each instance belongs to one processor state. Resize stages a replacement,
// so rollback never shares a mutated dynamic array with the old state.
internal sealed class ManagedTabStops
{
    [InlineArray(8)]
    private struct InlineWords { private ulong _element0; }

    private InlineWords _inline;
    private readonly ulong[]? _overflow;

    internal ManagedTabStops(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        Columns = columns;
        if (columns > 512) _overflow = new ulong[(columns - 513) / 64 + 1];
    }

    internal int Columns { get; }
    internal int PackedByteLength => (Columns - 1) / 8 + 1;
    internal int StorageByteLength => 64 + (_overflow?.Length ?? 0) * 8;
    private int WordCount => (Columns - 1) / 64 + 1;

    internal void Clear()
    {
        _inline = default;
        _overflow.AsSpan().Clear();
    }

    internal void ResetDefaults()
    {
        Clear();
        for (int i = 0; i < WordCount; i++) Word(i) = 0x0101010101010101UL;
        _inline[0] &= ~1UL;
        MaskUnusedBits();
        // Native defaults exclude the first and last columns. Both remain
        // legal explicit stops (HTS/CTC and restored snapshots).
        Remove(Columns - 1);
    }

    internal bool Contains(int column) => (uint)column < (uint)Columns &&
        (ReadWord(column / 64) & (1UL << (column % 64))) != 0;

    internal void Add(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        Word(column / 64) |= 1UL << (column % 64);
    }

    internal void Remove(int column)
    {
        if ((uint)column < (uint)Columns) Word(column / 64) &= ~(1UL << (column % 64));
    }

    // Inclusive bounds, -1 for no stop. Whole empty words are skipped; the
    // cursor's margin fallback is deliberately left to the processor.
    internal int FindNext(int start, int end)
    {
        start = Math.Max(start, 0);
        end = Math.Min(end, Columns - 1);
        if (start > end) return -1;
        int word = start / 64, last = end / 64;
        ulong bits = ReadWord(word) & (ulong.MaxValue << (start % 64));
        while (true)
        {
            if (word == last) bits &= ulong.MaxValue >> (63 - end % 64);
            if (bits != 0) return word * 64 + BitOperations.TrailingZeroCount(bits);
            if (++word > last) return -1;
            bits = ReadWord(word);
        }
    }

    internal int FindPrevious(int start, int end)
    {
        start = Math.Min(start, Columns - 1);
        end = Math.Max(end, 0);
        if (start < end) return -1;
        int word = start / 64, last = end / 64;
        ulong bits = ReadWord(word) & (ulong.MaxValue >> (63 - start % 64));
        while (true)
        {
            if (word == last) bits &= ulong.MaxValue << (end % 64);
            if (bits != 0) return word * 64 + 63 - BitOperations.LeadingZeroCount(bits);
            if (--word < last) return -1;
            bits = ReadWord(word);
        }
    }

    internal void LoadPacked(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PackedByteLength) throw new ArgumentException("Incorrect tabstop bitmap length.", nameof(bytes));
        Clear();
        int word = 0;
        while (bytes.Length >= 8)
        {
            Word(word++) = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            bytes = bytes[8..];
        }
        if (!bytes.IsEmpty)
        {
            ulong value = 0;
            for (int i = 0; i < bytes.Length; i++) value |= (ulong)bytes[i] << (i * 8);
            Word(word) = value;
        }
        MaskUnusedBits();
    }

    internal void CopyPackedBytes(int byteOffset, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteOffset, PackedByteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(destination.Length, PackedByteLength - byteOffset);
        while (!destination.IsEmpty)
        {
            ulong word = ReadWord(byteOffset / 8);
            if (byteOffset % 8 == 0 && destination.Length >= 8)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination, word);
                destination = destination[8..];
                byteOffset += 8;
            }
            else
            {
                destination[0] = (byte)(word >> ((byteOffset % 8) * 8));
                destination = destination[1..];
                byteOffset++;
            }
        }
    }

    private void MaskUnusedBits()
    {
        int validBits = Columns % 64;
        if (validBits != 0) Word(WordCount - 1) &= (1UL << validBits) - 1;
    }

    private ulong ReadWord(int index) => index < 8 ? _inline[index] : _overflow![index - 8];

    private ref ulong Word(int index)
    {
        if (index < 8) return ref _inline[index];
        return ref _overflow![index - 8];
    }
}
