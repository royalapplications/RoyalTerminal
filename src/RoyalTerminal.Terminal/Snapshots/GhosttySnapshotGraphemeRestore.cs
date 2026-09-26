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
        Span<uint> suffix = stackalloc uint[TerminalGraphemeStorage.MaximumSuffixCodepoints];
        int count = 0;
        for (int i = 0; i < encoded.Length && count < suffix.Length; i += 4)
        {
            uint cp = BinaryPrimitives.ReadUInt32LittleEndian(encoded[i..]);
            if (cp is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) continue;
            suffix[count++] = cp;
        }
        if (count == 0 || !TryStore(cellIndex, count)) return;
        if (count > maximumCodepoints - _codepoints)
            throw new InvalidDataException("Snapshot live graphemes exceed the configured codepoint limit.");
        _codepoints += count;
        // Reuse the validated wire array for the ordinary first entry. A
        // duplicate accepted only after native allocation failure owns a small
        // separate array; never substitute it into the lossless raw grid.
        _suffixes.Add(cellIndex, rawSuffix ?? suffix[..count].ToArray());
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
