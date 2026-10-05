// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalPasswordInputState
{
    /// <inheritdoc />
    public bool PasswordInput
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _terminal.PasswordInput;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _terminal.PasswordInput = value;
        }
    }
}
