// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Configures cursor defaults used by DECSCUSR zero and terminal resets.</summary>
public interface ITerminalCursorDefaults
{
    /// <summary>Sets the default shape, applying it immediately only while the cursor follows defaults.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The shape is not a defined cursor style.</exception>
    void SetDefaultCursorStyle(TerminalCursorStyle style);

    /// <summary>Sets default blinking, applying it immediately only while the cursor follows defaults.</summary>
    void SetDefaultCursorBlink(bool blink);
}
