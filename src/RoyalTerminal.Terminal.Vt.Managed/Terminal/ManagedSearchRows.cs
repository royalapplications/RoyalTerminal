// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Immutable, block-shared search row references. Updating an active row copies
/// its small reference block, not a history-sized flat array. Cell arrays retain
/// their existing COW ownership; no writable row/block storage is exposed.
/// </summary>
internal sealed class ManagedSearchRows
{
    private const int BlockSize = 64;
    private readonly TerminalRow[][] _blocks;
    private readonly int _offset;

    private ManagedSearchRows(TerminalRow[][] blocks, int offset, int length)
    {
        _blocks = blocks;
        _offset = offset;
        Length = length;
    }

    internal int Length { get; }

    internal TerminalRow this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
            long position = (long)_offset + index;
            return _blocks[(int)(position / BlockSize)][(int)(position % BlockSize)];
        }
    }

    internal static ManagedSearchRows Capture(TerminalScreen screen, ManagedSearchRows? previous,
        ConditionalWeakTable<object, TerminalRow> storage)
    {
        int count = screen.TotalRows;
        int blockCount = (int)(((long)count + BlockSize - 1) / BlockSize);
        bool sameShape = previous is not null && previous._offset == 0 && previous.Length == count;
        TerminalRow[][]? blocks = sameShape ? null : new TerminalRow[blockCount][];
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            int start = blockIndex * BlockSize;
            int length = Math.Min(BlockSize, count - start);
            TerminalRow[]? oldBlock = previous?.GetAlignedBlock(start, length);
            if (oldBlock is not null)
            {
                int same = 0;
                while (same < length && screen.GetRow(start + same).HasSameSearchContent(oldBlock[same])) same++;
                if (same == length)
                {
                    if (blocks is not null) blocks[blockIndex] = oldBlock;
                    continue;
                }
            }
            blocks ??= (TerminalRow[][])previous!._blocks.Clone();
            TerminalRow[] block = new TerminalRow[length];
            for (int offset = 0; offset < length; offset++)
            {
                int index = start + offset;
                TerminalRow live = screen.GetRow(index);
                TerminalRow? frozen = previous is not null && index < previous.Length ? previous[index] : null;
                if (frozen is null || !live.HasSameSearchContent(frozen))
                {
                    object identity = live.SearchStorageIdentity;
                    if (!storage.TryGetValue(identity, out frozen) || !live.HasSameSearchContent(frozen))
                    {
                        frozen = live.CreateStateCopy();
                        // The cache is shared across captures, but keys are weak:
                        // discarded row storage is not rooted by its own frozen
                        // value. Layout changes replace a cached view, never edit it.
                        storage.AddOrUpdate(identity, frozen);
                    }
                }
                block[offset] = frozen;
            }
            blocks[blockIndex] = block;
        }
        return blocks is null ? previous! : new(blocks, 0, count);
    }

    internal int CommonPrefix(ManagedSearchRows other)
    {
        int count = Math.Min(Length, other.Length);
        int row = 0;
        while (row < count)
        {
            int length = Math.Min(BlockSize, count - row);
            TerminalRow[]? block = GetAlignedBlock(row, length);
            if (block is not null && ReferenceEquals(block, other.GetAlignedBlock(row, length)))
            {
                row += length;
                continue;
            }
            if (!this[row].HasSameSearchContent(other[row])) break;
            row++;
        }
        return row;
    }

    private TerminalRow[]? GetAlignedBlock(int start, int length)
    {
        if (start > Length || length > Length - start) return null;
        long position = (long)_offset + start;
        return position % BlockSize == 0 ? _blocks[(int)(position / BlockSize)] : null;
    }

    internal ManagedSearchRows Slice(int start)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, Length);
        int length = Length - start;
        if (length == 0) return new([], 0, 0);
        long position = (long)_offset + start;
        int offset = (int)(position % BlockSize);
        int count = (int)(((long)offset + length + BlockSize - 1) / BlockSize);
        // Retain only the intersecting blocks, not the entire history through
        // the original outer array. At most one boundary block is extra.
        TerminalRow[][] blocks = _blocks.AsSpan((int)(position / BlockSize), count).ToArray();
        return new(blocks, offset, length);
    }
}
