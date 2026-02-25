// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Optional cursor-style source abstraction.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Normalized cursor style used by terminal controls and renderers.
/// </summary>
public enum TerminalCursorStyle : byte
{
    /// <summary>Filled block cursor.</summary>
    Block = 0,

    /// <summary>Underline cursor.</summary>
    Underline = 1,

    /// <summary>Vertical bar cursor.</summary>
    Bar = 2,

    /// <summary>Hollow block cursor.</summary>
    BlockHollow = 3,
}

/// <summary>
/// Optional VT processor capability that exposes normalized cursor style state.
/// </summary>
public interface ITerminalCursorStyleSource
{
    /// <summary>Current cursor style.</summary>
    TerminalCursorStyle CursorStyle { get; }

    /// <summary>
    /// Whether the active cursor style requests blink timing.
    /// </summary>
    bool CursorBlinking { get; }
}
