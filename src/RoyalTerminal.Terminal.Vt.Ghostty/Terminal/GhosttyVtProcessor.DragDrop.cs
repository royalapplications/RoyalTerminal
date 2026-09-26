// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    /// <inheritdoc />
    public bool IsDropRegistered => _terminal.GetDragDropState().Registered;
    /// <inheritdoc />
    public string[] RegisteredDropMimeTypes => _terminal.GetRegisteredDropMimeTypes();
    /// <inheritdoc />
    public TerminalDropOperation? AcceptedDropOperation => _terminal.GetDragDropState().AcceptedOperation is int value ? (TerminalDropOperation)value : null;
    /// <inheritdoc />
    public byte[] DragMove(in TerminalDropPosition position, IReadOnlyList<string> mimeTypes)
    {
        ArgumentNullException.ThrowIfNull(mimeTypes);
        GhosttyDropItem[] items = new GhosttyDropItem[mimeTypes.Count];
        for (int i = 0; i < items.Length; i++) items[i] = new(mimeTypes[i], ReadOnlyMemory<byte>.Empty);
        return _terminal.SendDragDropEvent(1, position.Column, position.Row, position.PixelX, position.PixelY, (uint)position.Operations & 3, items);
    }
    /// <inheritdoc />
    public byte[] DragLeave() => _terminal.SendDragDropEvent(3);
    /// <inheritdoc />
    public byte[] Drop(in TerminalDropPosition position, IReadOnlyList<TerminalDropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        GhosttyDropItem[] native = new GhosttyDropItem[Math.Min(items.Count, 16)];
        for (int i = 0; i < native.Length; i++) native[i] = new(items[i].MimeType, items[i].Data);
        return _terminal.SendDragDropEvent(2, position.Column, position.Row, position.PixelX, position.PixelY, (uint)position.Operations & 3, native);
    }
    /// <inheritdoc />
    public void CancelDrop() => _terminal.SendDragDropEvent(4);
}
