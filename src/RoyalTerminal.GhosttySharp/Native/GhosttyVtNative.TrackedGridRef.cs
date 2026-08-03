// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [LibraryImport(LibName, EntryPoint = "ghostty_tracked_grid_ref_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TrackedGridRefFree(nint reference);

    [LibraryImport(LibName, EntryPoint = "ghostty_tracked_grid_ref_has_value")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool TrackedGridRefHasValue(nint reference);

    [LibraryImport(LibName, EntryPoint = "ghostty_tracked_grid_ref_point")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TrackedGridRefPoint(
        nint reference,
        GhosttyPointTag tag,
        GhosttyPointCoordinate* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_tracked_grid_ref_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TrackedGridRefSet(
        nint reference,
        nint terminal,
        GhosttyPoint point);

    [LibraryImport(LibName, EntryPoint = "ghostty_tracked_grid_ref_snapshot")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TrackedGridRefSnapshot(
        nint reference,
        ref GhosttyGridRef snapshot);
}
