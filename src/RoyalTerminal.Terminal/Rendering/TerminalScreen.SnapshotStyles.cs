// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    private GhosttySnapshotStyleTracker? _snapshotStyleTracker;

    internal bool TracksSnapshotStyles => _snapshotScrollbackQuota is not null || _snapshotRowGeometry;

    internal void SnapshotStyleChanged(int key, int cursorRow, GhosttySnapshotStyle previous, GhosttySnapshotStyle current)
    {
        if (!TracksSnapshotStyles) return;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows) return;
        TerminalRow row = rows[rows.Count - ViewportRows + cursorRow];
        int alignment = _snapshotScrollbackQuota?.PageAlignment ??
            (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096);
        GhosttySnapshotAllocation layout = new(alignment);
        if (row.SnapshotAllocation is null) _ = GhosttySnapshotLiveAllocation.Measure(this, rows, layout);
        (_snapshotStyleTracker ??= new()).ChangeCursor(rows, key, row, previous, current, layout);
    }

    internal void SnapshotStyleAllocationReplaced(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
        => _snapshotStyleTracker?.AllocationReplaced(previous, replacement);
}
