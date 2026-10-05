// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void CollectUnusedHyperlinks()
    {
        try
        {
            _screen.CollectUnusedHyperlinks([_currentHyperlinkId, _snapshotPrimaryHyperlink, _snapshotAlternateHyperlink]);
        }
        catch (OutOfMemoryException)
        {
            // Optional pressure cleanup prepares before publication. Failure
            // leaves all identities intact and can be retried at the next gate;
            // it must not fault an otherwise successfully processed input batch.
        }
    }
}
