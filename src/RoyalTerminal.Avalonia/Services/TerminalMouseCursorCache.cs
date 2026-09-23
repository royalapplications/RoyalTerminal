// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

// UI-thread owned; bounded by Avalonia's standard cursor registry, not by OSC
// input. Keep native cursor resources alive until the control detaches.
internal sealed class TerminalMouseCursorCache : IDisposable
{
    private readonly Dictionary<StandardCursorType, Cursor> _cursors = new();

    internal Cursor Get(TerminalMouseShape shape, bool overLink)
    {
        StandardCursorType type = overLink ? StandardCursorType.Hand : Map(shape);
        if (!_cursors.TryGetValue(type, out Cursor? cursor)) _cursors.Add(type, cursor = new Cursor(type));
        return cursor;
    }

    internal bool Owns(Cursor? cursor)
    {
        foreach (Cursor owned in _cursors.Values)
            if (ReferenceEquals(cursor, owned)) return true;
        return false;
    }

    public void Dispose()
    {
        foreach (Cursor cursor in _cursors.Values) cursor.Dispose();
        _cursors.Clear();
    }

    internal static StandardCursorType Map(TerminalMouseShape shape) => shape switch
    {
        TerminalMouseShape.Default or TerminalMouseShape.ContextMenu => StandardCursorType.Arrow,
        TerminalMouseShape.Help => StandardCursorType.Help,
        TerminalMouseShape.Pointer or TerminalMouseShape.Grab or TerminalMouseShape.Grabbing => StandardCursorType.Hand,
        TerminalMouseShape.Progress => StandardCursorType.AppStarting,
        TerminalMouseShape.Wait => StandardCursorType.Wait,
        TerminalMouseShape.Cell or TerminalMouseShape.Crosshair or TerminalMouseShape.ZoomIn or TerminalMouseShape.ZoomOut => StandardCursorType.Cross,
        TerminalMouseShape.Text or TerminalMouseShape.VerticalText => StandardCursorType.Ibeam,
        TerminalMouseShape.Alias => StandardCursorType.DragLink,
        TerminalMouseShape.Copy => StandardCursorType.DragCopy,
        TerminalMouseShape.Move => StandardCursorType.DragMove,
        TerminalMouseShape.NoDrop or TerminalMouseShape.NotAllowed => StandardCursorType.No,
        TerminalMouseShape.AllScroll => StandardCursorType.SizeAll,
        TerminalMouseShape.ColResize or TerminalMouseShape.EwResize => StandardCursorType.SizeWestEast,
        TerminalMouseShape.RowResize or TerminalMouseShape.NsResize => StandardCursorType.SizeNorthSouth,
        TerminalMouseShape.NResize => StandardCursorType.TopSide,
        TerminalMouseShape.EResize => StandardCursorType.RightSide,
        TerminalMouseShape.SResize => StandardCursorType.BottomSide,
        TerminalMouseShape.WResize => StandardCursorType.LeftSide,
        TerminalMouseShape.NeResize or TerminalMouseShape.NeswResize => StandardCursorType.TopRightCorner,
        TerminalMouseShape.NwResize or TerminalMouseShape.NwseResize => StandardCursorType.TopLeftCorner,
        TerminalMouseShape.SeResize => StandardCursorType.BottomRightCorner,
        TerminalMouseShape.SwResize => StandardCursorType.BottomLeftCorner,
        _ => StandardCursorType.Ibeam,
    };
}
