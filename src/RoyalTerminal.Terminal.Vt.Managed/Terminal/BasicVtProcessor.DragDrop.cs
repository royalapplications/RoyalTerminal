// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private ManagedDragDropProtocol? _dragDrop;
    /// <inheritdoc />
    public bool IsDropRegistered => _dragDrop?.IsDropRegistered == true;
    /// <inheritdoc />
    public string[] RegisteredDropMimeTypes => _dragDrop?.RegisteredDropMimeTypes ?? [];
    /// <inheritdoc />
    public TerminalDropOperation? AcceptedDropOperation => _dragDrop?.AcceptedDropOperation;
    /// <inheritdoc />
    public byte[] DragMove(in TerminalDropPosition position, IReadOnlyList<string> mimeTypes) => _dragDrop?.DragMove(position, mimeTypes) ?? [];
    /// <inheritdoc />
    public byte[] DragLeave() => _dragDrop?.DragLeave() ?? [];
    /// <inheritdoc />
    public byte[] Drop(in TerminalDropPosition position, IReadOnlyList<TerminalDropItem> items) => _dragDrop?.Drop(position, items) ?? [];
    /// <inheritdoc />
    public void CancelDrop() => _dragDrop?.CancelDrop();
}
