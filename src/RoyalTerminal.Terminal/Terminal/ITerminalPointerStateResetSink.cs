// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Optional host lifecycle notification for loss of pointer ownership.</summary>
public interface ITerminalPointerStateResetSink
{
    /// <summary>
    /// Clears physical buttons and motion deduplication without emitting synthetic PTY input.
    /// Hosts call this on capture loss, detach, or input-session replacement, serialized with input.
    /// Terminal-requested mouse modes are unchanged.
    /// </summary>
    void ResetPointerState();
}
