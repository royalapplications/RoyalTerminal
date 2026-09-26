// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Input;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

// Composed input behavior: no view code-behind, shell command construction or
// OS move/delete effects. The host offers copy only because its drag session
// ends before a terminal client can conclude an asynchronous protocol transfer.
internal sealed class TerminalDragDropBehavior
{
    private readonly TerminalControl _control;
    private IDataTransfer? _transfer;
    private TerminalDropDataReader.Format[] _formats = [];
    private string[] _mimeTypes = [];

    internal TerminalDragDropBehavior(TerminalControl control)
    {
        _control = control;
        control.SetCurrentValue(DragDrop.AllowDropProperty, true);
        control.AddHandler(DragDrop.DragEnterEvent, Move);
        control.AddHandler(DragDrop.DragOverEvent, Move);
        control.AddHandler(DragDrop.DragLeaveEvent, Leave);
        control.AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Move(object? sender, DragEventArgs e)
    {
        if (_control.Screen is not { } screen || _control.ActiveVtProcessor is not ITerminalDragDropTarget target) return;
        long session = _control.DropSessionGeneration;
        lock (screen.SyncRoot) { if (!target.IsDropRegistered) return; }
        e.Handled = true;
        DragDropEffects allowed = e.DragEffects;
        e.DragEffects = DragDropEffects.None;
        byte[] response = [];
        try
        {
            CacheFormats(e.DataTransfer);
            lock (screen.SyncRoot)
            {
                if (!ReferenceEquals(_control.ActiveVtProcessor, target) || session != _control.DropSessionGeneration || !target.IsDropRegistered) return;
                if (_formats.Length == 0 || (allowed & DragDropEffects.Copy) == 0) { target.CancelDrop(); return; }
                response = target.DragMove(Position(e), _mimeTypes);
                TerminalDropOperation? accepted = target.AcceptedDropOperation;
                if (accepted is null or TerminalDropOperation.Copy) e.DragEffects = DragDropEffects.Copy;
            }
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            lock (screen.SyncRoot) if (ReferenceEquals(_control.ActiveVtProcessor, target) && session == _control.DropSessionGeneration) target.CancelDrop();
            ClearCache();
        }
        if (response.Length > 0) _control.SendInput(response);
    }

    private void Leave(object? sender, DragEventArgs e)
    {
        byte[] response = [];
        if (_control.Screen is { } screen && _control.ActiveVtProcessor is ITerminalDragDropTarget target)
        {
            lock (screen.SyncRoot)
            {
                if (target.IsDropRegistered) { response = target.DragLeave(); e.Handled = true; }
            }
        }
        ClearCache();
        if (response.Length > 0) _control.SendInput(response);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (_control.Screen is not { } screen || _control.ActiveVtProcessor is not ITerminalDragDropTarget target) return;
        long session = _control.DropSessionGeneration;
        lock (screen.SyncRoot) { if (!target.IsDropRegistered) return; }
        e.Handled = true;
        DragDropEffects allowed = e.DragEffects;
        e.DragEffects = DragDropEffects.None;
        byte[] response = [];
        try
        {
            CacheFormats(e.DataTransfer);
            if (_formats.Length == 0 || (allowed & DragDropEffects.Copy) == 0)
            {
                lock (screen.SyncRoot) target.CancelDrop();
                return;
            }
            // OS data retrieval can call into the toolkit; keep it outside the VT lock.
            TerminalDropItem[] items = TerminalDropDataReader.Read(e.DataTransfer, _formats);
            lock (screen.SyncRoot)
            {
                if (!ReferenceEquals(_control.ActiveVtProcessor, target) || session != _control.DropSessionGeneration || !target.IsDropRegistered) return;
                if (target.AcceptedDropOperation is TerminalDropOperation accepted && accepted != TerminalDropOperation.Copy) { target.CancelDrop(); return; }
                response = target.Drop(Position(e), items);
                e.DragEffects = DragDropEffects.Copy;
            }
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            lock (screen.SyncRoot) if (ReferenceEquals(_control.ActiveVtProcessor, target) && session == _control.DropSessionGeneration) target.CancelDrop();
        }
        finally { ClearCache(); }
        if (response.Length > 0) _control.SendInput(response);
    }

    internal void Cancel()
    {
        if (_control.Screen is { } screen && _control.ActiveVtProcessor is ITerminalDragDropTarget target)
            lock (screen.SyncRoot) target.CancelDrop();
        ClearCache();
    }

    private TerminalDropPosition Position(DragEventArgs e)
    {
        Point p = e.GetPosition(_control);
        Thickness padding = _control.Padding;
        double x = Math.Max(0, p.X - Math.Max(0, padding.Left));
        double y = Math.Max(0, p.Y - Math.Max(0, padding.Top));
        double cellWidth = Math.Max(1, _control.Renderer?.CellWidth ?? 1);
        double cellHeight = Math.Max(1, _control.Renderer?.CellHeight ?? 1);
        // Use the renderer-grid pixel space also reported by terminal resize,
        // not an additional desktop DPI multiplier applied only to this event.
        return new((uint)Math.Clamp(x / cellWidth, 0, Math.Max(0, _control.Columns - 1)),
            (uint)Math.Clamp(y / cellHeight, 0, Math.Max(0, _control.Rows - 1)),
            (int)Math.Clamp(x, 0, int.MaxValue), (int)Math.Clamp(y, 0, int.MaxValue), TerminalDropOperation.Copy);
    }

    private void CacheFormats(IDataTransfer transfer)
    {
        if (ReferenceEquals(_transfer, transfer)) return;
        _transfer = transfer;
        _formats = TerminalDropDataReader.GetFormats(transfer);
        _mimeTypes = new string[_formats.Length];
        for (int i = 0; i < _formats.Length; i++) _mimeTypes[i] = _formats[i].Mime;
    }

    private void ClearCache() { _transfer = null; _formats = []; _mimeTypes = []; }
    private static bool IsRecoverable(Exception error) => error is IOException or ArgumentException or InvalidOperationException or NotSupportedException;
}
