// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Optional capability for configuring VT mode reset policy.</summary>
public interface ITerminalModeDefaults
{
    /// <summary>
    /// Sets the current and reset-default value of an ANSI or DEC private mode.
    /// Does not replay VT commands, execute transition effects, or change saved
    /// mode values. Policy survives full reset and session preparation.
    /// Returns false without mutation for unknown or transition-dependent modes.
    /// Like other processor mutations, callers must serialize access with input.
    /// </summary>
    bool TrySetDefaultMode(int mode, bool enabled, bool ansi = false);
}
