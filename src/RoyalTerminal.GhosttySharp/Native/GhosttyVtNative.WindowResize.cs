// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Borrowed CSI 8 t callback. Zero dimensions preserve their current size. Never throw or re-enter native code.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RoyalWindowResizeCallback(nint terminal, nint userdata, ushort rows, ushort columns);

    /// <summary>Registers a borrowed window-resize callback pointer. Root it until unregistration or terminal disposal.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_window_resize_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SetRoyalWindowResizeCallback(nint terminal, nint userdata, nint callback);
}
