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
    private readonly GhosttySnapshotGraphemeStorage _storage = new(capacityBytes);
    private readonly Dictionary<int, uint[]> _suffixes = [];
    private int _codepoints;

    // Parsing transfers this seed to the immutable PAGE. Live owners must copy
    // it before mutation; rebuilding from final text would lose fragmentation.
    internal IReadOnlyDictionary<int, uint[]> Suffixes => _suffixes;
    internal GhosttySnapshotGraphemeStorage Storage => _storage;

    internal void Read(int cellIndex, ReadOnlySpan<byte> encoded, uint[]? rawSuffix)
    {
        // Only a successfully stored suffix makes duplicates first-wins. A
        // failed entry cleared its prefix and a later duplicate may still fit.
        if (_suffixes.ContainsKey(cellIndex)) return;
        if (rawSuffix is not null)
        {
            // The grid already filtered this owned array in wire order. Reuse
            // its valid prefix count instead of decoding the same bytes again.
            // The allocator still visits every replacement boundary and keeps
            // the native 64-suffix live limit independently of raw retention.
            int accepted = Math.Min(rawSuffix.Length, TerminalGraphemeStorage.MaximumSuffixCodepoints);
            if (TryAccept(cellIndex, accepted)) _suffixes.Add(cellIndex, rawSuffix);
            return;
        }
        Span<uint> suffix = stackalloc uint[TerminalGraphemeStorage.MaximumSuffixCodepoints];
        int count = 0;
        for (int i = 0; i < encoded.Length && count < suffix.Length; i += 4)
        {
            uint cp = BinaryPrimitives.ReadUInt32LittleEndian(encoded[i..]);
            if (cp is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) continue;
            suffix[count++] = cp;
        }
        if (TryAccept(cellIndex, count)) _suffixes.Add(cellIndex, suffix[..count].ToArray());
    }

    private bool TryAccept(int cellIndex, int count)
    {
        if (count == 0 || !TryStore(cellIndex, count)) return false;
        if (count > maximumCodepoints - _codepoints)
            throw new InvalidDataException("Snapshot live graphemes exceed the configured codepoint limit.");
        _codepoints += count;
        // A duplicate accepted after allocation failure owns its separate small
        // array; it never substitutes that retry into the lossless raw grid.
        return true;
    }

    private bool TryStore(int cellIndex, int codepoints)
    {
        if (_storage.AppendToLength(cellIndex, codepoints) != GhosttySnapshotGraphemeAddResult.Success)
        {
            _storage.Clear(cellIndex);
            return false; // Drop the complete entry, not a stored prefix.
        }
        return true;
    }
}
