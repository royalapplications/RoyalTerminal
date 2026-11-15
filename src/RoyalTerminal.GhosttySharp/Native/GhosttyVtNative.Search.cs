// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttySearchStatus : int
    {
        Running = 0,
        FeedRequired = 1,
        Complete = 2,
    }

    public enum GhosttySearchScroll : int
    {
        IfNeeded = 0,
        None = 1,
    }

    public enum GhosttySearchData : int
    {
        Status = 1,
        Needle = 2,
        TotalMatches = 3,
        SelectedIndex = 4,
        SelectedMatch = 5,
        Matches = 6,
        ViewportMatches = 7,
        SelectScroll = 8,
        /// <summary>Upstream INVALID value.</summary>
        Invalid = 0,
    }

    public enum GhosttySearchOption : int
    {
        Needle = 1,

        SelectScroll = 4,
        /// <summary>Upstream INVALID value.</summary>
        Invalid = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttySelectionBuffer
    {
        public GhosttySelectionRange* Pointer;
        public nuint Capacity;
        public nuint Length;
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_search_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SearchNew(nint allocator, out nint search, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SearchFree(nint search);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_tick")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SearchTick(nint search, out GhosttySearchStatus status);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_feed")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SearchFeed(nint search);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_run")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SearchRun(nint search);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SearchSet(nint search, GhosttySearchOption option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SearchGet(nint search, GhosttySearchData data, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_search_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SearchGetMulti(
        nint search,
        nuint count,
        GhosttySearchData* keys,
        void** values,
        nuint* written);
}
