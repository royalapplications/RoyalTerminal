// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty bitmap_allocator.zig. See ../THIRD-PARTY-NOTICES.txt.

using System.Numerics;

namespace RoyalTerminal.Terminal.Snapshots;

// Logical BitmapAllocator units, not a native backing allocation. Unmaterialized
// words are free. Small spans stay within a word; larger spans may cross words.
internal sealed class GhosttySnapshotBitmap
{
    internal readonly record struct Slice(int Start, int Chunks);
    private readonly int _chunkBytes;
    private readonly int _wordCount;
    private readonly List<ulong> _words = [];
    private int _searchStart;

    internal GhosttySnapshotBitmap(uint capacityBytes, int chunkBytes)
    {
        if (chunkBytes is not (16 or 32)) throw new ArgumentOutOfRangeException(nameof(chunkBytes));
        _chunkBytes = chunkBytes;
        _wordCount = checked((int)(GhosttySnapshotAllocation.BitmapDataBytes(capacityBytes, (ulong)chunkBytes) / ((ulong)chunkBytes * 64)));
    }

    private GhosttySnapshotBitmap(GhosttySnapshotBitmap source)
    {
        _chunkBytes = source._chunkBytes;
        _wordCount = source._wordCount;
        _searchStart = source._searchStart;
        _words.AddRange(source._words);
    }

    internal GhosttySnapshotBitmap Copy() => new(this);

    internal bool TryAllocate(int bytes, out Slice slice)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 1);
        int chunks = checked((int)(((long)bytes + _chunkBytes - 1) / _chunkBytes));
        int start = chunks <= 64 ? FindSmall(chunks) : FindLarge(chunks);
        if (start < 0) { slice = default; return false; }
        slice = new(start, chunks);
        Set(slice, allocated: true);
        while (_searchStart < _words.Count && _words[_searchStart] == ulong.MaxValue) _searchStart++;
        return true;
    }

    internal void Free(Slice slice)
    {
        if (slice.Chunks == 0) return;
        Set(slice, allocated: false);
        _searchStart = Math.Min(_searchStart, slice.Start / 64);
    }

    private int FindSmall(int chunks)
    {
        for (int word = _searchStart; word < _wordCount; word++)
        {
            ulong free = ~Word(word), starts = free;
            for (int shift = 1; shift < chunks && starts != 0; shift++) starts &= free >> shift;
            if (starts != 0) return checked(word * 64 + BitOperations.TrailingZeroCount(starts));
        }
        return -1;
    }

    private int FindLarge(int chunks)
    {
        int word = _searchStart;
        while (word < _wordCount)
        {
            int prefix = BitOperations.LeadingZeroCount(Word(word));
            if (prefix == 0) { word++; continue; }
            int start = checked(word * 64 + 64 - prefix);
            int remaining = chunks - prefix;
            word++;
            while (remaining > 64 && word < _wordCount && Word(word) == 0)
            {
                remaining -= 64;
                word++;
            }
            if (word == _wordCount) return -1;
            if (remaining <= 64 && BitOperations.TrailingZeroCount(Word(word)) >= remaining) return start;
            // Retry at the obstructing word, as native findFreeChunks does.
        }
        return -1;
    }

    private ulong Word(int index) => index < _words.Count ? _words[index] : 0;

    private void Set(Slice slice, bool allocated)
    {
        int word = slice.Start / 64, bit = slice.Start % 64, remaining = slice.Chunks;
        while (remaining > 0)
        {
            int count = Math.Min(remaining, 64 - bit);
            ulong mask = (ulong.MaxValue >> (64 - count)) << bit;
            while (word >= _words.Count) _words.Add(0);
            _words[word] = allocated ? _words[word] | mask : _words[word] & ~mask;
            remaining -= count;
            word++;
            bit = 0;
        }
    }
}
