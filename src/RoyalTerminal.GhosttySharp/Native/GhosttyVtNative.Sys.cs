// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyAllocatorVtable
    {
        public delegate* unmanaged[Cdecl]<void*, nuint, byte, nuint, void*> Alloc;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, nuint, byte> Resize;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, nuint, void*> Remap;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, byte, nuint, void> Free;
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

    public enum GhosttySysOption : int
    {
        Userdata = 0,
        DecodePng = 1,
        Log = 2,
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
