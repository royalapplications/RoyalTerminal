// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Borrowed OSC 99 slices; kind 0 ST, 1 BEL, 2 RIS with null/empty slices. Never throw across this boundary.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void RoyalNotificationCallback(nint terminal, nint userdata,
        byte* metadata, nuint metadataLength, byte* payload, nuint payloadLength, byte kind);

    /// <summary>Registers a borrowed callback pointer. Caller roots it until unregistered or the terminal is freed.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_notification_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SetRoyalNotificationCallback(nint terminal, nint userdata, nint callback);
}
