// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    internal CursorResizeLease? BeginCursorResize(TerminalScreen screen, TerminalRowBuffer rows, int key,
        TerminalRow row, ref int token, GhosttySnapshotAllocation layout)
    {
        if (row.SnapshotAllocation is not { MetadataOverflow: false } page) return null;
        List<TerminalRow> group = Group(rows, page);
        State state = Writable(page, group);
        if (!Synchronize(ref page, state, group, layout, screen)) return null;
        token = CursorHyperlinkToken(key, token);
        GhosttySnapshotStyle pen = state.Storage.Styles.Cursor;
        // Construct the lease before detaching references: allocation failure
        // must leave the original cursor intact. The lease is resize-scoped,
        // never retained by the tracker or a published screen.
        CursorResizeLease lease = new(this, screen, key, state.Storage.AllocationIdentity, pen, token);
        lease.StyleId = state.Storage.Styles.SuspendCursorReference();
        lease.HyperlinkId = state.Storage.Hyperlinks.SuspendCursorReference();
        if (key == 0) _primaryLinkToken = 0; else _alternateLinkToken = 0;
        return lease;
    }

    // Native uses a node pointer plus serial. Our immutable allocation identity
    // also distinguishes COW/reuse from real page replacement. Lookup is scoped
    // to live rows in this buffer, so retained publications and dead source
    // tables cannot accidentally satisfy the survival check.
    private bool FindResizeStorage(TerminalScreen screen, int key, object identity,
        out GhosttySnapshotPageAllocation? page, out State? state)
    {
        TerminalRowBuffer? rows = screen.GetSnapshotRows(key);
        if (rows is not null)
        {
            GhosttySnapshotPageAllocation? previous = null;
            for (int i = 0; i < rows.Count; i++)
            {
                GhosttySnapshotPageAllocation? candidate = rows[i].SnapshotAllocation;
                if (candidate is null || ReferenceEquals(previous, candidate)) continue;
                previous = candidate;
                if (_pages.TryGetValue(candidate, out State? known) && ReferenceEquals(known.Storage.AllocationIdentity, identity))
                {
                    page = candidate;
                    state = Exclusive(candidate, known);
                    return true;
                }
            }
        }
        page = null; state = null;
        return false;
    }

    internal sealed class CursorResizeLease : IDisposable
    {
        private GhosttySnapshotPageTracker? _owner;
        private readonly TerminalScreen _screen;
        private readonly int _key, _token;
        private readonly object _identity;
        private readonly GhosttySnapshotStyle _pen;
        internal int StyleId, HyperlinkId;

        internal CursorResizeLease(GhosttySnapshotPageTracker owner, TerminalScreen screen, int key,
            object identity, GhosttySnapshotStyle pen, int token)
        { _owner = owner; _screen = screen; _key = key; _identity = identity; _pen = pen; _token = token; }

        // Must run after prompt erasure, not merely after row remapping.
        internal int Complete(int cursorRow, ref uint counter)
        {
            GhosttySnapshotPageTracker owner = _owner ?? throw new InvalidOperationException("Resize cursor lease already completed.");
            int token = _screen.RestoreSnapshotResizeCursor(_key, cursorRow, _pen, _token, ref counter);
            if (owner.FindResizeStorage(_screen, _key, _identity, out _, out State? state))
            {
                state!.Storage.Styles.ReleaseTableReference(StyleId);
                state.Storage.Hyperlinks.ReleaseTableReference(HyperlinkId);
            }
            _owner = null;
            return token;
        }

        public void Dispose()
        {
            GhosttySnapshotPageTracker? owner = _owner;
            _owner = null;
            if (owner is null) return;
            if (owner.FindResizeStorage(_screen, _key, _identity, out GhosttySnapshotPageAllocation? page, out State? state))
            {
                // Before destructive page replacement, failure can restore the
                // exact IDs without insertion, string scratch or counter changes.
                state!.Storage.Styles.RestoreCursorReference(StyleId);
                state.Storage.Hyperlinks.RestoreCursorReference(HyperlinkId);
                if (_key == 0)
                { owner._primaryCursor = owner._primaryLinkPage = page; owner._primaryLinkToken = _token; }
                else
                { owner._alternateCursor = owner._alternateLinkPage = page; owner._alternateLinkToken = _token; }
            }
            else
            {
                // Row rollback is outside this metadata lease.
                // Never install old IDs into a replacement allocation; let the
                // next cursor observation reattach the processor's owned values.
                if (_key == 0)
                { owner._primaryCursor = owner._primaryLinkPage = null; owner._primaryLinkToken = 0; }
                else
                { owner._alternateCursor = owner._alternateLinkPage = null; owner._alternateLinkToken = 0; }
            }
        }
    }
}
