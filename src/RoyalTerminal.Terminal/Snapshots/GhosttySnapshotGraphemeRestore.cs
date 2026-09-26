// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;

namespace RoyalTerminal.Terminal.Snapshots;

// Models page.appendGrapheme while snapshot/grid.zig consumes suffix entries.
// Native capacity is a logical limit, not an allocation request: bitmap words
// are materialized only as bounded input actually occupies them. Small native
// bitmap allocations never cross a word boundary, even when adjacent words have
// enough aggregate free space. This fragmentation affects restored cell content.
internal sealed class GhosttySnapshotGraphemeRestore(uint capacityBytes, int maximumCodepoints)
{
    private readonly ulong _mapCapacity = GhosttySnapshotAllocation.GraphemeCellCapacity(capacityBytes);
    private readonly ulong _bitmapCount = GhosttySnapshotAllocation.BitmapDataBytes(capacityBytes, 16) / 1024;
    private readonly List<ulong> _words = [];
    private readonly Dictionary<int, uint[]> _suffixes = [];
    private int _searchStart;
    private int _codepoints;

    // Retain only the result after parsing, not the temporary bitmap model.
    internal IReadOnlyDictionary<int, uint[]> Suffixes => _suffixes;

    internal void Read(int cellIndex, ReadOnlySpan<byte> encoded, uint[]? rawSuffix)
    {
        // Only a successfully stored suffix makes duplicates first-wins. A
        // failed entry cleared its prefix and a later duplicate may still fit.
        if (_suffixes.ContainsKey(cellIndex)) return;
        Span<uint> suffix = stackalloc uint[TerminalGraphemeStorage.MaximumSuffixCodepoints];
        int count = 0;
        for (int i = 0; i < encoded.Length && count < suffix.Length; i += 4)
        {
            uint cp = BinaryPrimitives.ReadUInt32LittleEndian(encoded[i..]);
            if (cp is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) continue;
            suffix[count++] = cp;
        }
        if (count == 0 || !TryStore(count)) return;
        if (count > maximumCodepoints - _codepoints)
            throw new InvalidDataException("Snapshot live graphemes exceed the configured codepoint limit.");
        _codepoints += count;
        // Reuse the validated wire array for the ordinary first entry. A
        // duplicate accepted only after native allocation failure owns a small
        // separate array; never substitute it into the lossless raw grid.
        _suffixes.Add(cellIndex, rawSuffix ?? suffix[..count].ToArray());
    }

    private bool TryStore(int codepoints)
    {
        if ((ulong)_suffixes.Count >= _mapCapacity) return false;
        int previous = -1;
        int chunks = (codepoints + 3) / 4;
        for (int count = 1; count <= chunks; count++)
        {
            int next = Allocate(count);
            if (next < 0)
            {
                if (previous >= 0) Free(previous, count - 1);
                return false; // Drop the complete entry, not a stored prefix.
            }
            if (previous >= 0) Free(previous, count - 1);
            previous = next;
        }
        return true;
    }

    private int Allocate(int chunks)
    {
        for (int word = _searchStart; (ulong)word < _bitmapCount; word++)
        {
            if (word == _words.Count) _words.Add(0);
            ulong free = ~_words[word];
            ulong starts = free;
            for (int shift = 1; shift < chunks && starts != 0; shift++) starts &= free >> shift;
            if (starts == 0) continue;
            int bit = BitOperations.TrailingZeroCount(starts);
            _words[word] |= ((1UL << chunks) - 1) << bit;
            while (_searchStart < _words.Count && _words[_searchStart] == ulong.MaxValue) _searchStart++;
            return checked(word * 64 + bit);
        }
        return -1;
    }

    private void Free(int offset, int chunks)
    {
        int word = offset / 64;
        _words[word] &= ~(((1UL << chunks) - 1) << (offset % 64));
        _searchStart = Math.Min(_searchStart, word);
    }
}
