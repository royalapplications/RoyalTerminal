// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Snapshot charset registry, independent of parser implementation enums.</summary>
internal readonly record struct GhosttySnapshotCharset
{
    private GhosttySnapshotCharset(ushort bits) => Bits = bits;

    internal ushort Bits { get; }
    internal int GL => (Bits >> 8) & 3;
    internal int GR => (Bits >> 10) & 3;
    internal int? SingleShift => ((Bits >> 12) & 7) is 0 ? null : ((Bits >> 12) & 7) - 1;

    internal int GetCharset(int slot)
    {
        if ((uint)slot >= 4) throw new ArgumentOutOfRangeException(nameof(slot));
        return (Bits >> (slot * 2)) & 3;
    }

    internal static GhosttySnapshotCharset Read(ushort bits)
    {
        // All four charset/GL/GR values are defined; only the optional single
        // shift and padding require normalization in snapshot version one.
        bits &= 0x7FFF;
        if (((bits >> 12) & 7) > 4) bits &= 0x0FFF;
        return new(bits);
    }
}
