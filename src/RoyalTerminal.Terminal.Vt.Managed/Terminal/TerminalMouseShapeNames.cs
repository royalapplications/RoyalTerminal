// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

internal static class TerminalMouseShapeNames
{
    internal static bool TryParse(ReadOnlySpan<byte> name, out TerminalMouseShape shape)
    {
        switch (name.Length)
        {
            case 4:
                if (name.SequenceEqual("help"u8)) { shape = TerminalMouseShape.Help; return true; }
                if (name.SequenceEqual("wait"u8)) { shape = TerminalMouseShape.Wait; return true; }
                if (name.SequenceEqual("cell"u8)) { shape = TerminalMouseShape.Cell; return true; }
                if (name.SequenceEqual("text"u8)) { shape = TerminalMouseShape.Text; return true; }
                if (name.SequenceEqual("copy"u8)) { shape = TerminalMouseShape.Copy; return true; }
                if (name.SequenceEqual("move"u8)) { shape = TerminalMouseShape.Move; return true; }
                if (name.SequenceEqual("grab"u8)) { shape = TerminalMouseShape.Grab; return true; }
                if (name.SequenceEqual("hand"u8)) { shape = TerminalMouseShape.Pointer; return true; }
                break;
            case 5:
                if (name.SequenceEqual("alias"u8)) { shape = TerminalMouseShape.Alias; return true; }
                if (name.SequenceEqual("watch"u8)) { shape = TerminalMouseShape.Wait; return true; }
                if (name.SequenceEqual("cross"u8)) { shape = TerminalMouseShape.Crosshair; return true; }
                if (name.SequenceEqual("xterm"u8)) { shape = TerminalMouseShape.Text; return true; }
                if (name.SequenceEqual("hand1"u8)) { shape = TerminalMouseShape.Grab; return true; }
                if (name.SequenceEqual("fleur"u8)) { shape = TerminalMouseShape.AllScroll; return true; }
                break;
            case 7:
                if (name.SequenceEqual("default"u8)) { shape = TerminalMouseShape.Default; return true; }
                if (name.SequenceEqual("pointer"u8)) { shape = TerminalMouseShape.Pointer; return true; }
                if (name.SequenceEqual("no-drop"u8)) { shape = TerminalMouseShape.NoDrop; return true; }
                if (name.SequenceEqual("zoom-in"u8)) { shape = TerminalMouseShape.ZoomIn; return true; }
                break;
            case 8:
                if (name.SequenceEqual("progress"u8)) { shape = TerminalMouseShape.Progress; return true; }
                if (name.SequenceEqual("grabbing"u8)) { shape = TerminalMouseShape.Grabbing; return true; }
                if (name.SequenceEqual("n-resize"u8)) { shape = TerminalMouseShape.NResize; return true; }
                if (name.SequenceEqual("e-resize"u8)) { shape = TerminalMouseShape.EResize; return true; }
                if (name.SequenceEqual("s-resize"u8)) { shape = TerminalMouseShape.SResize; return true; }
                if (name.SequenceEqual("w-resize"u8)) { shape = TerminalMouseShape.WResize; return true; }
                if (name.SequenceEqual("zoom-out"u8)) { shape = TerminalMouseShape.ZoomOut; return true; }
                if (name.SequenceEqual("left_ptr"u8)) { shape = TerminalMouseShape.Default; return true; }
                if (name.SequenceEqual("dnd-link"u8)) { shape = TerminalMouseShape.Alias; return true; }
                if (name.SequenceEqual("dnd-copy"u8)) { shape = TerminalMouseShape.Copy; return true; }
                if (name.SequenceEqual("dnd-move"u8)) { shape = TerminalMouseShape.Move; return true; }
                if (name.SequenceEqual("top_side"u8)) { shape = TerminalMouseShape.NResize; return true; }
                break;
            case 9:
                if (name.SequenceEqual("crosshair"u8)) { shape = TerminalMouseShape.Crosshair; return true; }
                if (name.SequenceEqual("ne-resize"u8)) { shape = TerminalMouseShape.NeResize; return true; }
                if (name.SequenceEqual("nw-resize"u8)) { shape = TerminalMouseShape.NwResize; return true; }
                if (name.SequenceEqual("se-resize"u8)) { shape = TerminalMouseShape.SeResize; return true; }
                if (name.SequenceEqual("sw-resize"u8)) { shape = TerminalMouseShape.SwResize; return true; }
                if (name.SequenceEqual("ew-resize"u8)) { shape = TerminalMouseShape.EwResize; return true; }
                if (name.SequenceEqual("ns-resize"u8)) { shape = TerminalMouseShape.NsResize; return true; }
                if (name.SequenceEqual("left_side"u8)) { shape = TerminalMouseShape.WResize; return true; }
                break;
            case 10:
                if (name.SequenceEqual("all-scroll"u8)) { shape = TerminalMouseShape.AllScroll; return true; }
                if (name.SequenceEqual("col-resize"u8)) { shape = TerminalMouseShape.ColResize; return true; }
                if (name.SequenceEqual("row-resize"u8)) { shape = TerminalMouseShape.RowResize; return true; }
                if (name.SequenceEqual("right_side"u8)) { shape = TerminalMouseShape.EResize; return true; }
                break;
            case 11:
                if (name.SequenceEqual("not-allowed"u8)) { shape = TerminalMouseShape.NotAllowed; return true; }
                if (name.SequenceEqual("nesw-resize"u8)) { shape = TerminalMouseShape.NeswResize; return true; }
                if (name.SequenceEqual("nwse-resize"u8)) { shape = TerminalMouseShape.NwseResize; return true; }
                if (name.SequenceEqual("dnd-no-drop"u8)) { shape = TerminalMouseShape.NoDrop; return true; }
                if (name.SequenceEqual("bottom_side"u8)) { shape = TerminalMouseShape.SResize; return true; }
                break;
            case 12:
                if (name.SequenceEqual("context-menu"u8)) { shape = TerminalMouseShape.ContextMenu; return true; }
                break;
            case 13:
                if (name.SequenceEqual("vertical-text"u8)) { shape = TerminalMouseShape.VerticalText; return true; }
                break;
            case 14:
                if (name.SequenceEqual("question_arrow"u8)) { shape = TerminalMouseShape.Help; return true; }
                if (name.SequenceEqual("left_ptr_watch"u8)) { shape = TerminalMouseShape.Progress; return true; }
                if (name.SequenceEqual("crossed_circle"u8)) { shape = TerminalMouseShape.NotAllowed; return true; }
                break;
            case 15:
                if (name.SequenceEqual("top_left_corner"u8)) { shape = TerminalMouseShape.NwResize; return true; }
                break;
            case 16:
                if (name.SequenceEqual("top_right_corner"u8)) { shape = TerminalMouseShape.NeResize; return true; }
                break;
            case 18:
                if (name.SequenceEqual("bottom_left_corner"u8)) { shape = TerminalMouseShape.SwResize; return true; }
                break;
            case 19:
                if (name.SequenceEqual("bottom_right_corner"u8)) { shape = TerminalMouseShape.SeResize; return true; }
                break;
        }
        shape = default;
        return false;
    }
}

