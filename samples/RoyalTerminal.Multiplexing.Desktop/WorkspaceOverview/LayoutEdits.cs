// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

internal enum LayoutTool { Tile, Float, Shelf, Move, Swap, Left, Right, Above, Below, Grow, Shrink, NewShellRight, NewShellBelow, NewWindow, RenameWindow, RenameBlock, EarlierWindow, LaterWindow, CloseWindow }
internal sealed record LayoutMutation(string Method, JsonElement Arguments);

/// <summary>Turn one explicit user gesture on an immutable snapshot into one compare-and-set request.</summary>
internal static class LayoutEdits
{
    internal static LayoutMutation Plan(MultiplexerSessionDescriptor s, Guid home, LayoutTool tool, Guid? subject = null,
        Guid? other = null, Guid? destination = null, string? label = null, MultiplexerFloatBounds? bounds = null)
    {
        var window = s.Windows.Single(w => w.Id == home);
        var a = new JsonObject { ["sessionId"] = s.Id.ToString("D"), ["expectedRevision"] = s.Revision };
        MultiplexerBlockDescriptor Block(Guid? id) => s.Blocks.SingleOrDefault(b => b.Id == id && b.SessionId == s.Id)
            ?? throw new ArgumentException("Select a terminal.");
        string Label() => !string.IsNullOrWhiteSpace(label) && label.Trim().Length <= 256 ? label.Trim() : throw new ArgumentException("Enter a label (1–256 characters).");
        Guid Destination() => destination is Guid id && s.Windows.Any(w => w.Id == id) ? id : throw new ArgumentException("Select a destination window.");
        LayoutMutation Send(string name)
        {
            using var json = JsonDocument.Parse(a.ToJsonString());
            return new(name, json.RootElement.Clone());
        }
        a["windowId"] = home.ToString("D");
        switch (tool)
        {
            case LayoutTool.NewWindow: a.Remove("windowId"); a["label"] = Label(); return Send("session.new_window");
            case LayoutTool.RenameWindow: a["label"] = Label(); return Send("session.set_window_label");
            case LayoutTool.EarlierWindow: case LayoutTool.LaterWindow:
                int position = Array.FindIndex(s.Windows.ToArray(), w => w.Id == home) + (tool == LayoutTool.EarlierWindow ? 0 : 2);
                if (position < 1 || position > s.Windows.Count) throw new ArgumentException("The window is already at that end.");
                a["position"] = position; return Send("session.move_window");
            case LayoutTool.CloseWindow:
                if (s.Windows.Count == 1) throw new ArgumentException("Cannot remove the last window.");
                if (s.Blocks.Any(b => b.WindowId == home)) { var target = Destination(); if (target == home) throw new ArgumentException("Choose another window to preserve these terminals."); a["preserveIn"] = target.ToString("D"); }
                return Send("session.close_window");
            case LayoutTool.NewShellRight: case LayoutTool.NewShellBelow:
                var spec = new JsonObject { ["windowId"] = home.ToString("D"), ["label"] = string.IsNullOrWhiteSpace(label) ? "Shell" : Label(), ["orientation"] = tool == LayoutTool.NewShellRight ? "horizontal" : "vertical", ["expectedRevision"] = s.Revision };
                if (subject.HasValue) { var anchor = Block(subject); if (anchor.WindowId != home || anchor.Placement != MultiplexerPaneKind.Tiled) throw new ArgumentException("Select a tiled split anchor, or clear the selection."); spec["splitBlockId"] = anchor.Id.ToString("D"); }
                a.Remove("windowId"); a.Remove("expectedRevision"); a["block"] = spec; return Send("block.create");
        }
        var block = Block(subject);
        if (block.WindowId != home) throw new ArgumentException("The selected terminal has moved.");
        a["blockId"] = block.Id.ToString("D");
        switch (tool)
        {
            case LayoutTool.RenameBlock: a.Remove("windowId"); a["label"] = Label(); return Send("session.set_block_label");
            case LayoutTool.Grow: case LayoutTool.Shrink:
                if (window.Layout is null || block.Placement != MultiplexerPaneKind.Tiled) throw new ArgumentException("Select a tiled terminal in a split.");
                var layout = ResizeParent(window.Layout, block.Id, tool == LayoutTool.Grow ? .05 : -.05);
                a.Remove("blockId"); a["layout"] = JsonSerializer.SerializeToNode(layout, OverviewJson.Default.MultiplexerLayoutNode); return Send("session.set_layout");
            case LayoutTool.Swap:
                var second = Block(other); if (second.Id == block.Id) throw new ArgumentException("Select a different terminal to swap with.");
                a.Remove("blockId"); a.Remove("windowId"); a["firstId"] = block.Id.ToString("D"); a["secondId"] = second.Id.ToString("D"); return Send("block.swap");
            case LayoutTool.Left: case LayoutTool.Right: case LayoutTool.Above: case LayoutTool.Below:
                var relative = Block(other); if (relative.Id == block.Id || relative.Placement != MultiplexerPaneKind.Tiled) throw new ArgumentException("Choose a different tiled terminal as the target.");
                a["windowId"] = relative.WindowId.ToString("D"); a["relativeTo"] = relative.Id.ToString("D"); a["before"] = tool is LayoutTool.Left or LayoutTool.Above;
                a["orientation"] = tool is LayoutTool.Left or LayoutTool.Right ? "horizontal" : "vertical"; a["kind"] = "tiled"; break;
            case LayoutTool.Move: a["windowId"] = Destination().ToString("D"); a["kind"] = "tiled"; break;
            case LayoutTool.Tile: a["kind"] = "tiled"; break;
            case LayoutTool.Shelf: a["kind"] = "unplaced"; break;
            case LayoutTool.Float:
                a["kind"] = "floating";
                if (bounds is { } r) a["bounds"] = new JsonObject { ["x"] = r.X, ["y"] = r.Y, ["width"] = r.Width, ["height"] = r.Height };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(tool));
        }
        return Send("block.place");
    }

    private static MultiplexerLayoutNode ResizeParent(MultiplexerLayoutNode root, Guid block, double delta)
    {
        var result = Visit(root); return result ?? throw new ArgumentException("This terminal has no parent split.");
        MultiplexerLayoutNode? Visit(MultiplexerLayoutNode n)
        {
            if (n.First is not { } a || n.Second is not { } b) return null;
            if (a.BlockId == block || b.BlockId == block)
                return n with { Ratio = Math.Clamp(n.Ratio + (a.BlockId == block ? delta : -delta), .01, .99) };
            if (Visit(a) is { } left) return n with { First = left };
            return Visit(b) is { } right ? n with { Second = right } : null;
        }
    }

    internal static MultiplexerFloatBounds DragFloat(MultiplexerFloatBounds r, Vector delta, Size viewport, bool resize)
    {
        if (!(viewport.Width > 0 && viewport.Height > 0 && double.IsFinite(delta.X) && double.IsFinite(delta.Y))) throw new ArgumentException("Invalid drag coordinates.");
        int dx = (int)Math.Round(Math.Clamp(delta.X / viewport.Width, -1, 1) * 10000), dy = (int)Math.Round(Math.Clamp(delta.Y / viewport.Height, -1, 1) * 10000);
        return resize ? r with { Width = Math.Clamp(r.Width + dx, Math.Min(r.Width, 300), 10000-r.X), Height = Math.Clamp(r.Height + dy, Math.Min(r.Height, 300), 10000-r.Y) }
            : r with { X = Math.Clamp(r.X + dx, 0, 10000-r.Width), Y = Math.Clamp(r.Y + dy, 0, 10000-r.Height) };
    }

    internal static LayoutTool DropZone(Rect area, Point p)
    {
        double x = (p.X - area.X) / area.Width, y = (p.Y - area.Y) / area.Height;
        if (!area.Contains(p) || area.Width <= 0 || area.Height <= 0) throw new ArgumentException("Release over a terminal.");
        double edge = Math.Min(Math.Min(x, 1-x), Math.Min(y, 1-y));
        if (edge >= .25) return LayoutTool.Swap;
        return edge == x ? LayoutTool.Left : edge == 1-x ? LayoutTool.Right : edge == y ? LayoutTool.Above : LayoutTool.Below;
    }
}
