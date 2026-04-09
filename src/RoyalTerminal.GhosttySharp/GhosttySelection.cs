// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed selection range used by Ghostty VT formatter and Kitty graphics helpers.
/// </summary>
public readonly struct GhosttySelection
{
    /// <summary>
    /// Creates a selection from two grid references.
    /// </summary>
    public GhosttySelection(
        GhosttyVtNative.GhosttyGridRef start,
        GhosttyVtNative.GhosttyGridRef end,
        bool rectangle = false)
    {
        Start = start;
        End = end;
        Rectangle = rectangle;
    }

    /// <summary>Inclusive selection start.</summary>
    public GhosttyVtNative.GhosttyGridRef Start { get; }

    /// <summary>Inclusive selection end.</summary>
    public GhosttyVtNative.GhosttyGridRef End { get; }

    /// <summary>Whether the selection is rectangular.</summary>
    public bool Rectangle { get; }

    internal GhosttyVtNative.GhosttySelectionRange ToNative()
    {
        GhosttyVtNative.GhosttySelectionRange range = GhosttyVtNative.GhosttySelectionRange.CreateSized();
        range.Start = Start;
        range.End = End;
        range.Rectangle = Rectangle;
        return range;
    }
}
