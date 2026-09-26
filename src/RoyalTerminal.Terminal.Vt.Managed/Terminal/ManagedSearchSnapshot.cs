// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Search-only COW capture; contains no live screen, images or registries.</summary>
internal sealed class ManagedSearchSnapshot
{
    private Dictionary<object, TerminalRow>? _storage;

    private ManagedSearchSnapshot(TerminalRow[] rows, int columns, int viewportRows, bool alternate)
    {
        Rows = rows;
        Columns = columns;
        ViewportRows = viewportRows;
        Alternate = alternate;
    }

    internal TerminalRow[] Rows { get; }
    internal int Columns { get; }
    internal int ViewportRows { get; }
    internal bool Alternate { get; }

    /// <summary>Called under the live screen lock, without retained writable cell references.</summary>
    internal static ManagedSearchSnapshot Capture(TerminalScreen screen, ManagedSearchSnapshot? previous)
    {
        bool compatible = previous is not null && previous.Columns == screen.Columns &&
            previous.Alternate == screen.AlternateBufferActive;
        if (compatible && previous!.Rows.Length == screen.TotalRows && previous.ViewportRows == screen.ViewportRows)
        {
            int row = 0;
            while (row < screen.TotalRows && screen.GetRow(row).HasSameSearchContent(previous.Rows[row])) row++;
            if (row == screen.TotalRows) return previous;
        }

        TerminalRow[] rows = new TerminalRow[screen.TotalRows];
        for (int i = 0; i < rows.Length; i++)
        {
            TerminalRow live = screen.GetRow(i);
            TerminalRow? frozen = null;
            if (compatible)
            {
                if (i < previous!.Rows.Length && live.HasSameSearchContent(previous.Rows[i])) frozen = previous.Rows[i];
                else
                {
                    // Reuse immutable rows when eviction or history prepend shifts indices.
                    previous._storage ??= previous.IndexStorage();
                    if (previous._storage.TryGetValue(live.SearchStorageIdentity, out TerminalRow? candidate) &&
                        live.HasSameSearchContent(candidate)) frozen = candidate;
                }
            }
            rows[i] = frozen ?? live.CreateStateCopy();
        }
        return new(rows, screen.Columns, screen.ViewportRows, screen.AlternateBufferActive);
    }

    private Dictionary<object, TerminalRow> IndexStorage()
    {
        Dictionary<object, TerminalRow> result = new(Rows.Length, ReferenceEqualityComparer.Instance);
        foreach (TerminalRow row in Rows) result.TryAdd(row.SearchStorageIdentity, row);
        return result;
    }

    internal int CommonPrefix(ManagedSearchSnapshot? other)
    {
        if (other is null || Columns != other.Columns || Alternate != other.Alternate) return 0;
        int count = Math.Min(Rows.Length, other.Rows.Length);
        int row = 0;
        while (row < count && Rows[row].HasSameSearchContent(other.Rows[row])) row++;
        return row;
    }

    internal ManagedSearchSnapshot Slice(int start)
        => new(Rows[start..], Columns, ViewportRows, Alternate);
}
