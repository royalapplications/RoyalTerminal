// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Borrowed input representation; the native drop call copies both slices.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RoyalDndItem
    {
        /// <summary>Printable ASCII MIME token, without spaces.</summary>
        public byte* Mime;
        /// <summary>MIME byte count (1–1024).</summary>
        public nuint MimeLength;
        /// <summary>Representation bytes, nullable only for empty data.</summary>
        public byte* Data;
        /// <summary>Representation byte count.</summary>
        public nuint DataLength;
    }

    /// <summary>Queries registration and accepted operation (-1 unanswered, 0 none, 1 copy, 2 move).</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_dnd_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult DragDropState(nint terminal, out byte registered, out int accepted);

    /// <summary>Copies registration MIME bytes. OutOfSpace sets length without writing the buffer.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_dnd_mimes")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult DragDropMimes(nint terminal, byte* output, nuint capacity, out nuint length);

    /// <summary>Reports a host event: 1 move, 2 drop, 3 leave, 4 cancel held data, 5 unregister.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_dnd_event")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult DragDropEvent(nint terminal, uint kind,
        uint column, uint row, int pixelX, int pixelY, uint operations,
        RoyalDndItem* items, nuint count, GhosttyWriter writer);
}
