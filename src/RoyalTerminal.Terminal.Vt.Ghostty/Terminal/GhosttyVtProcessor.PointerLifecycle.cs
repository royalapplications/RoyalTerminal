// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalPointerStateResetSink
{
    /// <inheritdoc />
    public void ResetPointerState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pressedMouseButtons = 0;
        _mouseEncoder.Reset();
    }
}
