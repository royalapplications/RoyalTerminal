// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    // Kept separately from the style pin so a style-before-position transition
    // can release its old link and still detect the subsequent page crossing.
    private GhosttySnapshotPageAllocation? _primaryLinkPage, _alternateLinkPage;
    private int _primaryLinkToken, _alternateLinkToken;

    internal int CursorHyperlinkToken(int key, int fallback)
        => (key == 0 ? _primaryLinkPage : _alternateLinkPage) is null ? fallback
            : key == 0 ? _primaryLinkToken : _alternateLinkToken;

    internal void EndCursorHyperlink(int key)
    {
        GhosttySnapshotPageAllocation? page = key == 0 ? _primaryLinkPage : _alternateLinkPage;
        if (page is not null && _pages.TryGetValue(page, out State? state)) Exclusive(page, state).Storage.Hyperlinks.EndCursor();
        if (key == 0) _primaryLinkToken = 0; else _alternateLinkToken = 0;
    }

    internal void RestorePrunedCursor(TerminalScreen screen, TerminalRowBuffer rows, int key,
        GhosttySnapshotStyle pen, GhosttySnapshotAllocation layout)
    {
        GhosttySnapshotPageAllocation? previous = key == 0 ? _primaryCursor : _alternateCursor;
        if (previous is null || rows.Count == 0) return;
        // Usually the cursor is near the tail. Only a removed page requires
        // walking all remaining rows, which are already bounded by pruning.
        for (int index = rows.Count - 1; index >= 0; index--)
            if (ReferenceEquals(rows[index].SnapshotAllocation, previous)) return;
        if (rows[0].SnapshotAllocation is not { MetadataOverflow: false } page) return;

        // PageList moves a pruned pin onto the first survivor. Ghostty's
        // cursorPageReplaced restores its existing ID there without growing
        // metadata or consuming an implicit ID. cursorReload may subsequently
        // make a genuine page transition to the active area.
        int token = CursorHyperlinkToken(key, 0);
        using PageRowsLease lease = RentGroup(rows, page);
        State state = Writable(page, lease.Rows);
        if (!Synchronize(ref page, state, lease.Rows, layout, screen)) return;
        if (state.Storage.Styles.ChangeCursor(pen) != GhosttySnapshotSetAddResult.Success)
            _cursorStyleDrops |= (byte)(1 << key);
        byte[]? encoded = screen.SnapshotHyperlinkEncoding(token);
        if (encoded is null || state.Storage.Hyperlinks.StartCursor(encoded, encoded) != GhosttySnapshotHyperlinkAddResult.Success)
            token = 0;
        _pages.Remove(previous);
        if (key == 0)
        {
            _primaryCursor = _primaryLinkPage = page;
            _primaryLinkToken = token;
        }
        else
        {
            _alternateCursor = _alternateLinkPage = page;
            _alternateLinkToken = token;
        }
    }

    internal int ChangeHyperlink(TerminalScreen screen, TerminalRowBuffer rows, int key, TerminalRow row,
        int token, ref uint counter, bool restart, GhosttySnapshotAllocation layout)
    {
        if (row.SnapshotAllocation is not { MetadataOverflow: false } page) return token;
        GhosttySnapshotPageAllocation? departing = key == 0 ? _primaryLinkPage : _alternateLinkPage;
        if (!restart && ReferenceEquals(page, departing)) return CursorHyperlinkToken(key, token);
        if (!restart) token = CursorHyperlinkToken(key, token);
        EndCursorHyperlink(key);
        TerminalHyperlink? identity = null;
        bool reissue = !restart && departing is not null && token != 0 &&
            screen.TryGetHyperlink(token, out identity) && identity is { IsExplicit: false };
        bool created = false;
        if (reissue)
        {
            token = screen.RegisterHyperlink(identity!.UriBytes, default, counter, out created);
        }
        int candidate = token;
        bool accepted = false;
        try
        {
            using PageRowsLease lease = RentGroup(rows, page);
            List<TerminalRow> group = lease.Rows;
            State state = Writable(page, group);
            if (!Synchronize(ref page, state, group, layout, screen))
            {
                accepted = token != 0; // Host-owned overflow retains its logical token.
                return token;
            }
            byte[]? encoded = screen.SnapshotHyperlinkEncoding(token);
            if (encoded is not null)
            {
                while (true)
                {
                    GhosttySnapshotHyperlinkAddResult result = state.Storage.Hyperlinks.StartCursor(encoded, encoded);
                    if (result == GhosttySnapshotHyperlinkAddResult.Success) break;
                    if (!GrowMetadata(ref page, state, group, GhosttySnapshotHyperlinkStorage.GrowthDimension(result),
                        layout, preserveOnFailure: true))
                    {
                        token = 0;
                        break;
                    }
                }
            }
            else token = 0;
            if (reissue && token != 0) counter = unchecked(counter + 1);
            if (key == 0) { _primaryLinkPage = page; _primaryLinkToken = token; }
            else { _alternateLinkPage = page; _alternateLinkToken = token; }
            accepted = token != 0;
            return token;
        }
        finally
        {
            if (created && !accepted) screen.DiscardPendingHyperlink(candidate);
        }
    }
}
