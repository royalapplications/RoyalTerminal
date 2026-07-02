// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Shared terminal pane command contracts.

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Identifies pane split requests exposed by reusable terminal shells.
/// </summary>
public enum TerminalPaneSplitRequest
{
    /// <summary>
    /// Split the focused pane into side-by-side panes and place the new pane on the right.
    /// </summary>
    Right,

    /// <summary>
    /// Split the focused pane into stacked panes and place the new pane below.
    /// </summary>
    Down,
}

/// <summary>
/// Identifies directional pane focus or resize requests exposed by reusable terminal shells.
/// </summary>
public enum TerminalPaneDirection
{
    /// <summary>
    /// Move or resize toward the left.
    /// </summary>
    Left,

    /// <summary>
    /// Move or resize toward the right.
    /// </summary>
    Right,

    /// <summary>
    /// Move or resize upward.
    /// </summary>
    Up,

    /// <summary>
    /// Move or resize downward.
    /// </summary>
    Down,
}

/// <summary>
/// Identifies how a terminal pane is split into child panes.
/// </summary>
public enum TerminalPaneSplitOrientation
{
    /// <summary>
    /// Split panes side by side.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Split panes vertically into top and bottom children.
    /// </summary>
    Vertical,
}
