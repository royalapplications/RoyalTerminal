// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalVisibilityState
{
    /// <inheritdoc />
    public bool PotentiallyVisible
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _terminal.PotentiallyVisible;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            byte[] response = _terminal.SetVisibility(value);
            if (response.Length != 0) ResponseCallback?.Invoke(response);
        }
    }
}
