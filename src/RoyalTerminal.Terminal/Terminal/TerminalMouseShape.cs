// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Terminal-requested W3C mouse pointer shapes, in Ghostty snapshot-v1 registry order.</summary>
public enum TerminalMouseShape
{
    /// <summary>The default pointer shape.</summary>
    Default = 0,
    /// <summary>The context-menu pointer shape.</summary>
    ContextMenu = 1,
    /// <summary>The help pointer shape.</summary>
    Help = 2,
    /// <summary>The pointer pointer shape.</summary>
    Pointer = 3,
    /// <summary>The progress pointer shape.</summary>
    Progress = 4,
    /// <summary>The wait pointer shape.</summary>
    Wait = 5,
    /// <summary>The cell pointer shape.</summary>
    Cell = 6,
    /// <summary>The crosshair pointer shape.</summary>
    Crosshair = 7,
    /// <summary>The text pointer shape.</summary>
    Text = 8,
    /// <summary>The vertical-text pointer shape.</summary>
    VerticalText = 9,
    /// <summary>The alias pointer shape.</summary>
    Alias = 10,
    /// <summary>The copy pointer shape.</summary>
    Copy = 11,
    /// <summary>The move pointer shape.</summary>
    Move = 12,
    /// <summary>The no-drop pointer shape.</summary>
    NoDrop = 13,
    /// <summary>The not-allowed pointer shape.</summary>
    NotAllowed = 14,
    /// <summary>The grab pointer shape.</summary>
    Grab = 15,
    /// <summary>The grabbing pointer shape.</summary>
    Grabbing = 16,
    /// <summary>The all-scroll pointer shape.</summary>
    AllScroll = 17,
    /// <summary>The col-resize pointer shape.</summary>
    ColResize = 18,
    /// <summary>The row-resize pointer shape.</summary>
    RowResize = 19,
    /// <summary>The n-resize pointer shape.</summary>
    NResize = 20,
    /// <summary>The e-resize pointer shape.</summary>
    EResize = 21,
    /// <summary>The s-resize pointer shape.</summary>
    SResize = 22,
    /// <summary>The w-resize pointer shape.</summary>
    WResize = 23,
    /// <summary>The ne-resize pointer shape.</summary>
    NeResize = 24,
    /// <summary>The nw-resize pointer shape.</summary>
    NwResize = 25,
    /// <summary>The se-resize pointer shape.</summary>
    SeResize = 26,
    /// <summary>The sw-resize pointer shape.</summary>
    SwResize = 27,
    /// <summary>The ew-resize pointer shape.</summary>
    EwResize = 28,
    /// <summary>The ns-resize pointer shape.</summary>
    NsResize = 29,
    /// <summary>The nesw-resize pointer shape.</summary>
    NeswResize = 30,
    /// <summary>The nwse-resize pointer shape.</summary>
    NwseResize = 31,
    /// <summary>The zoom-in pointer shape.</summary>
    ZoomIn = 32,
    /// <summary>The zoom-out pointer shape.</summary>
    ZoomOut = 33,
}

/// <summary>Live terminal mouse shape, independent of reporting and render holds.</summary>
public interface ITerminalMouseShapeSource
{
    /// <summary>Gets the application-requested shape; the host may substitute a hyperlink pointer.</summary>
    TerminalMouseShape MouseShape { get; }
}

