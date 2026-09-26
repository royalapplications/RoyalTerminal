// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

internal enum GhosttySnapshotCapacityDimension
{
    Styles,
    GraphemeBytes,
    HyperlinkBytes,
    StringBytes,
}

// All counts are in native units, not CLR object sizes. GraphemeTemporaryBytes
// represents the largest old slice that must coexist with its replacement.
internal readonly record struct GhosttySnapshotMetadataUsage(
    ulong Styles, ulong GraphemeCells, ulong GraphemeBytes, ulong GraphemeTemporaryBytes,
    ulong Hyperlinks, ulong HyperlinkCells, ulong StringBytes);
