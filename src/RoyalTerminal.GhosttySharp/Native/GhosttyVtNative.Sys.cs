// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>
    /// Native allocation callbacks. Alignment arguments are base-2 exponents:
    /// a value n requests alignment 1 &lt;&lt; n bytes, not n bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyAllocatorVtable
    {
        public delegate* unmanaged[Cdecl]<void*, nuint, byte, nuint, void*> Alloc;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, nuint, byte> Resize;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, nuint, void*> Remap;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, void> Free;
        /// <summary>Reserved ABI slots; callers must initialize these to zero.</summary>
        public nint Reserved0;
        public nint Reserved1;
        public nint Reserved2;
        public nint Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyAllocator
    {
        public void* Context;
        public GhosttyAllocatorVtable* Vtable;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttySysImage
    {
        public uint Width;
        public uint Height;
        public byte* Data;
        public nuint DataLength;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate byte GhosttySysDecodePngCallback(
        void* userdata,
        GhosttyAllocator* allocator,
        byte* data,
        nuint dataLength,
        GhosttySysImage* output);

    public enum GhosttySysLogLevel : int
    {
        Error = 0,
        Warning = 1,
        Info = 2,
        Debug = 3,
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttySysLogCallback(
        void* userdata,
        GhosttySysLogLevel level,
        byte* scope,
        nuint scopeLength,
        byte* message,
        nuint messageLength);

    /// <summary>Fills the complete destination with cryptographically secure random bytes.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public unsafe delegate bool GhosttySysRandomSecureCallback(
        void* userdata,
        byte* buffer,
        nuint length);

    public enum GhosttySysOption : int
    {
        Userdata = 1,
        DecodePng = 2,
        Log = 3,
        RandomSecure = 4,
        /// <summary>Upstream INVALID value.</summary>
        Invalid = 0,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_sys_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SysSet(GhosttySysOption option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_sys_log_stderr")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void SysLogStderr(
        void* userdata,
        GhosttySysLogLevel level,
        byte* scope,
        nuint scopeLength,
        byte* message,
        nuint messageLength);

    [LibraryImport(LibName, EntryPoint = "ghostty_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial byte* Alloc(GhosttyAllocator* allocator, nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void Free(GhosttyAllocator* allocator, byte* ptr, nuint len);
}
