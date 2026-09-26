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
        if (reissue)
        {
            token = screen.RegisterHyperlink(identity!.UriBytes, default, counter);
        }
        List<TerminalRow> group = Group(rows, page);
        State state = Writable(page, group);
        if (!Synchronize(ref page, state, group, layout, screen)) return token;
        byte[]? encoded = screen.SnapshotHyperlinkEncoding(token);
        if (encoded is not null)
        {
            while (true)
            {
                GhosttySnapshotHyperlinkAddResult result = state.Storage.Hyperlinks.StartCursor(encoded, encoded);
                if (result == GhosttySnapshotHyperlinkAddResult.Success) break;
                if (!GrowMetadata(ref page, state, group, GhosttySnapshotHyperlinkStorage.GrowthDimension(result), layout)) return 0;
            }
        }
        else token = 0;
        if (reissue && token != 0) counter = unchecked(counter + 1);
        if (key == 0) { _primaryLinkPage = page; _primaryLinkToken = token; }
        else { _alternateLinkPage = page; _alternateLinkToken = token; }
        return token;
    }
}
