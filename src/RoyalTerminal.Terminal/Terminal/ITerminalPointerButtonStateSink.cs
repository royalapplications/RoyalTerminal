// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Tracks physical buttons independently of host reporting and selection policy.</summary>
public interface ITerminalPointerButtonStateSink
{
    /// <summary>
    /// Records a button transition even when its report is suppressed. Non-button events
    /// and unsupported buttons are ignored. Does not encode input or change motion history.
    /// Serialize with pointer encoding and terminal mutation.
    /// </summary>
    void ObservePointerButton(in TerminalPointerEvent pointerEvent);
}
