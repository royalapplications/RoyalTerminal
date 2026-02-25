// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Optional focus-event mode state source.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional mode-source capability for focus-event reporting state (DECSET 1004).
/// </summary>
public interface ITerminalFocusEventModeSource
{
    /// <summary>
    /// Gets whether focus event reporting mode is enabled.
    /// </summary>
    bool FocusEventsEnabled { get; }
}
