// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Search-only COW capture; contains no live screen, images or registries.</summary>
internal sealed class ManagedSearchSnapshot
{
    private readonly ConditionalWeakTable<object, TerminalRow> _storage;

    private ManagedSearchSnapshot(ManagedSearchRows rows, int columns, int viewportRows, bool alternate,
        ConditionalWeakTable<object, TerminalRow> storage)
    {
        Rows = rows;
        Columns = columns;
        ViewportRows = viewportRows;
        Alternate = alternate;
        _storage = storage;
    }

    internal ManagedSearchRows Rows { get; }
    internal int Columns { get; }
    internal int ViewportRows { get; }
    internal bool Alternate { get; }

    /// <summary>Called under the live screen lock, without retained writable cell references.</summary>
    internal static ManagedSearchSnapshot Capture(TerminalScreen screen, ManagedSearchSnapshot? previous)
    {
        bool compatible = previous is not null && previous.Columns == screen.Columns &&
            previous.Alternate == screen.AlternateBufferActive;
        ConditionalWeakTable<object, TerminalRow> storage = compatible ? previous!._storage : new();
        ManagedSearchRows rows = ManagedSearchRows.Capture(screen, compatible ? previous!.Rows : null, storage);
        if (compatible && ReferenceEquals(rows, previous!.Rows) && previous.ViewportRows == screen.ViewportRows)
            return previous;
        return new(rows, screen.Columns, screen.ViewportRows, screen.AlternateBufferActive, storage);
    }

    internal int CommonPrefix(ManagedSearchSnapshot? other)
    {
        if (other is null || Columns != other.Columns || Alternate != other.Alternate) return 0;
        return Rows.CommonPrefix(other.Rows);
    }

    internal ManagedSearchSnapshot Slice(int start)
        => new(Rows.Slice(start), Columns, ViewportRows, Alternate, _storage);
}
