// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalPointerStateResetSink
{
    /// <inheritdoc />
    public void ResetPointerState() => _mouseEncoder.Reset();
}
