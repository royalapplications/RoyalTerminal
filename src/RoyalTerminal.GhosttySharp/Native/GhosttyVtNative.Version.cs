// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Typed keys for reading and changing formatter options.</summary>
    public enum GhosttyFormatterOption : int
    {
        Invalid = 0,
        Emit = 1,
        Unwrap = 2,
        Trim = 3,
        Selection = 4,
        Extras = 5,
    }

    /// <summary>A caller-owned codepoint buffer; length reports the required capacity.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyCodepointBuffer
    {
        public uint* Ptr;
        public nuint Cap;
        public nuint Len;
    }

    /// <summary>Returns the process-lifetime UTF-8 semantic version string.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Version();

    /// <summary>Zeroes a sized structure and writes its size into the first field.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_sized_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void SizedInit(void* pointer, nuint size);

    /// <summary>Sets a formatter option, or resets it when value is null.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_formatter_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult FormatterSet(nint formatter, GhosttyFormatterOption option, void* value);

    /// <summary>Reads a formatter option into the storage required by its key.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_formatter_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult FormatterGet(nint formatter, GhosttyFormatterOption option, void* output);

    /// <summary>Selects the next search match, returning NoValue when none exists.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_search_select_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SearchSelectNext(nint search);

    /// <summary>Selects the previous search match, returning NoValue when none exists.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_search_select_prev")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SearchSelectPrev(nint search);
}
