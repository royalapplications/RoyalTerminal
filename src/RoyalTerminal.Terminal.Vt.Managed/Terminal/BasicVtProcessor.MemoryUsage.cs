// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalMemoryUsageSource
{
    /// <inheritdoc />
    public TerminalMemoryUsage GetMemoryUsage() => new(TerminalMemoryStorageKind.ManagedRows, true,
        MeasureScreenMemory(0, _primaryKittyStore), MeasureScreenMemory(1, _alternateKittyStore));

    private TerminalScreenMemoryUsage MeasureScreenMemory(int key, ManagedKittyGraphicsStore? images)
    {
        TerminalRowBuffer? rows = _screen.GetSnapshotRows(key);
        ulong logical = 0, resident = 0, compressed = 0, compressedRows = 0;
        if (rows is not null)
            for (int i = 0; i < rows.Count; i++)
            {
                TerminalRow row = rows[i];
                logical += row.LogicalCellBytes;
                resident += row.ResidentCellBytes;
                compressed += row.CompressedCellBytes;
                if (row.IsCompressed) compressedRows++;
            }
        return new((ulong)(rows?.Count ?? 0), logical, resident, compressedRows, compressed, (ulong)(images?.StoredBytes ?? 0));
    }

    internal void CompressSnapshotHistory(int key, int appliedRows)
    {
        TerminalRowBuffer? rows = _screen.GetSnapshotRows(key);
        if (rows is null) return;
        int count = Math.Min(appliedRows, rows.Count - _screen.ViewportRows);
        // Do not compress the visible scrollback viewport of the active screen.
        if ((_inAltScreen ? 1 : 0) == key) count = Math.Min(count, rows.Count - _screen.ViewportRows - _screen.ScrollOffset);
        for (int i = 0; i < count; i++) rows[i].TryCompressCells();
    }
}
