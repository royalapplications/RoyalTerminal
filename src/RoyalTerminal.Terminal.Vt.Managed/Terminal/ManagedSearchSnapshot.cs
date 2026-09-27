// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Search-only COW capture; contains no live screen, images or registries.</summary>
internal sealed class ManagedSearchSnapshot
{
    private readonly ConditionalWeakTable<object, TerminalRow> _storage;
    private readonly TerminalSearchChangeToken? _changes;
    private readonly ulong _revision;

    private ManagedSearchSnapshot(ManagedSearchRows rows, int columns, int viewportRows, bool alternate,
        ConditionalWeakTable<object, TerminalRow> storage, TerminalSearchChangeToken? changes = null)
    {
        Rows = rows;
        Columns = columns;
        ViewportRows = viewportRows;
        Alternate = alternate;
        _storage = storage;
        _changes = changes;
        _revision = changes?.Revision ?? 0;
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
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(screen.AlternateBufferActive ? 1 : 0)!
            .GetSearchChangeToken(Math.Max(0, screen.TotalRows - screen.ViewportRows));
        if (compatible && previous!.ViewportRows == screen.ViewportRows &&
            ReferenceEquals(changes, previous._changes) && changes.Matches(previous._revision))
            return previous;
        ConditionalWeakTable<object, TerminalRow> storage = compatible ? previous!._storage : new();
        int unchangedPrefix = compatible && ReferenceEquals(changes, previous!._changes)
            ? changes.UnchangedHistoryPrefix(previous._revision) : 0;
        ManagedSearchRows rows = ManagedSearchRows.Capture(screen, compatible ? previous!.Rows : null, storage,
            changes, unchangedPrefix);
        // Even a conservative invalidation publishes a new immutable stamp. Old
        // captures remain valid for independent consumers and worker threads.
        ManagedSearchSnapshot captured = new(rows, screen.Columns, screen.ViewportRows, screen.AlternateBufferActive, storage, changes);
        // Do not acknowledge invalidation before every fallible capture step has
        // succeeded: a retry must still see the prior capture's pending changes.
        changes.RecordCaptured();
        return captured;
    }

    internal int CommonPrefix(ManagedSearchSnapshot? other)
    {
        if (other is null || Columns != other.Columns || Alternate != other.Alternate) return 0;
        return Rows.CommonPrefix(other.Rows);
    }

    internal int PrependedRows(ManagedSearchSnapshot other, CancellationToken cancellation)
    {
        int added = Rows.Length - other.Rows.Length;
        if (added <= 0 || Columns != other.Columns || Alternate != other.Alternate) return 0;
        // Compare immutable storage/layout identities, not text hashes. Check
        // the active tail first so ordinary append/edit work rejects quickly.
        for (int row = other.Rows.Length - 1; row >= 0; row--)
        {
            if ((row & 255) == 0) cancellation.ThrowIfCancellationRequested();
            if (!Rows[row + added].HasSameSearchContent(other.Rows[row])) return 0;
        }
        return added;
    }

    internal ManagedSearchSnapshot Slice(int start)
        => new(Rows.Slice(start), Columns, ViewportRows, Alternate, _storage);
}
