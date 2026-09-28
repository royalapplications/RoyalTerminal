// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    // Caller holds the screen lock and supplies processor-held cursor tokens.
    // Only protocol-owned identities are eligible; public registrations remain
    // stable until reset. Hidden columns, both buffers and tracker cursor pins
    // are roots. A retained COW owner has an independent registry/root census.
    internal int CollectUnusedHyperlinks(ReadOnlySpan<int> retainedTokens, bool force = false)
    {
        ThrowIfSnapshotMutationFailed();
        try
        {
            HashSet<int>? candidates = _hyperlinkIdentities.BeginCollection(force);
            if (candidates is null) return 0;
            foreach (int token in retainedTokens) candidates.Remove(token);
            candidates.Remove(SnapshotCursorHyperlinkToken(0, 0));
            candidates.Remove(SnapshotCursorHyperlinkToken(1, 0));
            for (int key = 0; key < 2 && candidates.Count != 0; key++)
            {
                TerminalRowBuffer? rows = GetSnapshotRows(key);
                if (rows is null) continue;
                for (int row = 0; row < rows.Count && candidates.Count != 0; row++)
                    foreach (ref readonly TerminalCell cell in rows[row].ReadOnlyPreservedCells)
                        if (cell.HyperlinkId != 0) candidates.Remove(cell.HyperlinkId);
            }
            _hyperlinkIdentities.PrepareCollection(candidates);
            MutationCheckpoint?.Invoke(SnapshotMutationCheckpoint.HyperlinkCollectionPrepared);
            return _hyperlinkIdentities.CommitCollection(candidates, _hyperlinksById);
        }
        finally { _hyperlinkIdentities.CancelCollection(); }
    }
}
