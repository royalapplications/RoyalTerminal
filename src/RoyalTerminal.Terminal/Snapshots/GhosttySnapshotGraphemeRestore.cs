// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

// Models page.appendGrapheme while snapshot/grid.zig consumes suffix entries.
// Native capacity is a logical limit, not an allocation request: bitmap words
// are materialized only as bounded input actually occupies them. Small native
// bitmap allocations never cross a word boundary, even when adjacent words have
// enough aggregate free space. This fragmentation affects restored cell content.
internal sealed class GhosttySnapshotGraphemeRestore(uint capacityBytes, int maximumCodepoints)
{
    private readonly ulong _mapCapacity = GhosttySnapshotAllocation.GraphemeCellCapacity(capacityBytes);
    private readonly GhosttySnapshotBitmap _bitmap = new(capacityBytes, 16);
    private readonly Dictionary<int, uint[]> _suffixes = [];
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
        GhosttySnapshotBitmap.Slice previous = default;
        int chunks = (codepoints + 3) / 4;
        for (int count = 1; count <= chunks; count++)
        {
            if (!_bitmap.TryAllocate(count * 16, out GhosttySnapshotBitmap.Slice next))
            {
                _bitmap.Free(previous);
                return false; // Drop the complete entry, not a stored prefix.
            }
            _bitmap.Free(previous);
            previous = next;
        }
        return true;
    }
}
