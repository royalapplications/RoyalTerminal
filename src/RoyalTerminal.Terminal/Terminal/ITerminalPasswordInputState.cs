// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Host-reported password-input state. This is terminal metadata, not a security boundary or
/// an instruction to enable OS secure input. Hosts own termios detection and OS integration.
/// State is global, survives soft reset/buffer switches and clears on full/session reset.
/// Serialize reads and writes with processor operations.
/// </summary>
public interface ITerminalPasswordInputState
{
    /// <summary>Whether the host reports password entry (typically canonical input with echo disabled).</summary>
    bool PasswordInput { get; set; }
}
