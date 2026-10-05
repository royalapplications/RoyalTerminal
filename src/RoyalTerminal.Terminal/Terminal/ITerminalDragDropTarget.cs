// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Operations allowed by a drag source, or accepted by a terminal client.</summary>
[Flags]
public enum TerminalDropOperation
{
    /// <summary>No operation; the client rejected the drop.</summary>
    None = 0,
    /// <summary>Copy data without removing the source.</summary>
    Copy = 1,
    /// <summary>Move data. Hosts must only offer this when they can complete a move safely.</summary>
    Move = 2,
}

/// <summary>A drag position in zero-based cells and content-relative terminal pixels, matching resize reports.</summary>
/// <param name="Column">Nonnegative column.</param>
/// <param name="Row">Nonnegative row.</param>
/// <param name="PixelX">Content-relative horizontal pixel coordinate.</param>
/// <param name="PixelY">Content-relative vertical pixel coordinate.</param>
/// <param name="Operations">Operations offered by the source.</param>
public readonly record struct TerminalDropPosition(uint Column, uint Row, int PixelX, int PixelY, TerminalDropOperation Operations);

/// <summary>One dropped representation. The target copies the data before returning.</summary>
/// <param name="MimeType">Nonempty printable ASCII MIME token, without spaces or controls.</param>
/// <param name="Data">Bytes of this representation; never interpreted as terminal output.</param>
public sealed record TerminalDropItem(string MimeType, ReadOnlyMemory<byte> Data);

/// <summary>
/// Optional Kitty OSC 72 host integration. Serialize every call with processor access.
/// Returned bytes go to the client, not back through the VT parser. Registration
/// survives RIS but not a new session. Dropped data is retained until conclusion,
/// a new drag, cancellation, unregister or disposal. No remote files are fetched.
/// </summary>
public interface ITerminalDragDropTarget
{
    /// <summary>Whether a client has registered to accept protocol drops.</summary>
    bool IsDropRegistered { get; }
    /// <summary>Client-declared MIME types, copied for the host. Empty permits any offered type.</summary>
    string[] RegisteredDropMimeTypes { get; }
    /// <summary>Client feedback, or null while no complete acceptance has arrived.</summary>
    TerminalDropOperation? AcceptedDropOperation { get; }
    /// <summary>Reports a drag over the content and its representations in data-request index order.</summary>
    byte[] DragMove(in TerminalDropPosition position, IReadOnlyList<string> mimeTypes);
    /// <summary>Reports leaving, unless the data has already been dropped and is still being served.</summary>
    byte[] DragLeave();
    /// <summary>Captures up to 16 representations (64 MiB total) and reports the drop.</summary>
    byte[] Drop(in TerminalDropPosition position, IReadOnlyList<TerminalDropItem> items);
    /// <summary>Releases host drag/drop data without removing the client registration.</summary>
    void CancelDrop();
}
