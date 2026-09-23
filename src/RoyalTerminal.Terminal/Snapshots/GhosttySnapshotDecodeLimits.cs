// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Explicit limits independent of untrusted snapshot allocation hints and source policies.</summary>
internal sealed record GhosttySnapshotDecodeLimits(
    int MaximumPayloadBytes = 16 * 1024 * 1024,
    long MaximumTotalPayloadBytes = 64 * 1024 * 1024,
    int MaximumCells = 4 * 1024 * 1024,
    int MaximumPages = 65536,
    int MaximumSuffixCodepointsPerPage = 1024 * 1024,
    int MaximumStringBytesPerRecord = 4 * 1024 * 1024,
    int MaximumContinuationBytes = 64 * 1024)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumTotalPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumCells);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumPages);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSuffixCodepointsPerPage);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumStringBytesPerRecord);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumContinuationBytes);
    }
}
