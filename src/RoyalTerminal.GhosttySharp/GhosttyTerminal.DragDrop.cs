// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>A native drag/drop representation; copied by the terminal on drop.</summary>
/// <param name="MimeType">Printable ASCII MIME token, without spaces (1–1024 bytes).</param>
/// <param name="Data">Representation bytes.</param>
public sealed record GhosttyDropItem(string MimeType, ReadOnlyMemory<byte> Data);

public sealed partial class GhosttyTerminal
{
    /// <summary>Returns registration and client feedback; null means no complete acceptance.</summary>
    public (bool Registered, int? AcceptedOperation) GetDragDropState()
    {
        ThrowIfFailed(GhosttyVtNative.DragDropState(Handle, out byte registered, out int accepted), "ghostty_royal_dnd_state");
        return (registered != 0, accepted < 0 ? null : accepted);
    }

    /// <summary>Copies the client's registered MIME tokens. Serialize with terminal mutations.</summary>
    public unsafe string[] GetRegisteredDropMimeTypes()
    {
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.DragDropMimes(Handle, null, 0, out nuint length);
        if (result != GhosttyVtNative.GhosttyResult.OutOfSpace) ThrowIfFailed(result, "ghostty_royal_dnd_mimes");
        if (length == 0) return [];
        byte[] data = new byte[checked((int)length)];
        fixed (byte* pointer = data)
            ThrowIfFailed(GhosttyVtNative.DragDropMimes(Handle, pointer, length, out _), "ghostty_royal_dnd_mimes");
        return Encoding.UTF8.GetString(data).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Reports a host event (1 move, 2 drop, 3 leave, 4 cancel, 5 unregister) and
    /// returns protocol bytes to send to the client. Coordinates are zero-based
    /// cells and content-relative terminal pixels. Operations is copy=1/move=2.
    /// Up to 16 items and 64 MiB are copied; callers retain their input ownership.
    /// Serialize with terminal mutations. No callback or input pointer is retained.
    /// </summary>
    public unsafe byte[] SendDragDropEvent(uint kind, uint column = 0, uint row = 0,
        int pixelX = 0, int pixelY = 0, uint operations = 0, IReadOnlyList<GhosttyDropItem>? items = null)
    {
        nint handle = Handle;
        int count = Math.Min(items?.Count ?? 0, 16);
        if (kind is < 1 or > 5 || operations > 3 || (kind == 1 && items?.Count > 16))
            throw new ArgumentOutOfRangeException(nameof(kind));
        byte[][] mimeBytes = new byte[count][];
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            GhosttyDropItem item = items![i];
            ArgumentNullException.ThrowIfNull(item);
            if (string.IsNullOrEmpty(item.MimeType) || item.MimeType.Length > 1024)
                throw new ArgumentException("Invalid drop MIME type.", nameof(items));
            foreach (char c in item.MimeType) if (c < 33 || c > 126) throw new ArgumentException("Invalid drop MIME type.", nameof(items));
            mimeBytes[i] = Encoding.ASCII.GetBytes(item.MimeType);
            total += item.Data.Length;
        }
        if (total > 64 * 1024 * 1024) throw new ArgumentException("Drop data exceeds 64 MiB.", nameof(items));
        using NativeLifetimeLease lease = AcquireNativeLifetimeLease();
        using MemoryStream response = new();
        GhosttyStreamWriter.Write(response, writer =>
        {
            Span<GhosttyVtNative.RoyalDndItem> native = stackalloc GhosttyVtNative.RoyalDndItem[count];
            MemoryHandle[] pins = new MemoryHandle[count * 2];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    pins[i * 2] = mimeBytes[i].AsMemory().Pin();
                    pins[i * 2 + 1] = items![i].Data.Pin();
                    native[i] = new() { Mime = (byte*)pins[i * 2].Pointer, MimeLength = (nuint)mimeBytes[i].Length,
                        Data = (byte*)pins[i * 2 + 1].Pointer, DataLength = (nuint)items[i].Data.Length };
                }
                fixed (GhosttyVtNative.RoyalDndItem* pointer = native)
                    return GhosttyVtNative.DragDropEvent(handle, kind, column, row, pixelX, pixelY, operations, pointer, (nuint)count, writer);
            }
            finally { foreach (MemoryHandle pin in pins) pin.Dispose(); }
        }, "ghostty_royal_dnd_event");
        return response.ToArray();
    }
}
