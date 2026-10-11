// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalRow
{
    private TerminalCell[]? _residentCells;
    private CompressedTerminalRow? _compressedCells;
    private object StorageIdentity => (object?)_residentCells ?? _compressedCells!;
    private TerminalCell[] CellStorage
    {
        get => _residentCells ?? RestoreCompressedCells();
        set { _residentCells = value; _compressedCells = null; }
    }

    internal bool IsCompressed => _compressedCells is not null;
    internal ulong LogicalCellBytes => _compressedCells?.LogicalBytes ?? CompressedTerminalRow.Measure(_residentCells);
    internal ulong CompressedCellBytes => _compressedCells?.StoredBytes ?? 0;
    internal ulong ResidentCellBytes => _compressedCells?.StoredBytes ?? LogicalCellBytes;

    internal bool TryCompressCells()
    {
        if (_residentCells is null) return false;
        try
        {
            CompressedTerminalRow? compressed = CompressedTerminalRow.TryCreate(_residentCells);
            if (compressed is null) return false;
            // All fallible work precedes publication. Other COW wrappers keep
            // their arrays; compression neither edits cells nor metadata.
            InvalidateSearch();
            _compressedCells = compressed;
            _residentCells = null;
            CellsAreShared = false;
            return true;
        }
        catch (OutOfMemoryException) { return false; } // Optional optimization, like native.
    }

    private TerminalCell[] RestoreCompressedCells()
    {
        // Failure leaves the immutable compressed owner intact for retry.
        TerminalCell[] cells = _compressedCells!.Restore();
        CellStorage = cells;
        CellsAreShared = false;
        return cells;
    }
}
