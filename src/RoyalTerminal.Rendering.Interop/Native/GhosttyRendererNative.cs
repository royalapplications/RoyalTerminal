// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop - P/Invoke declarations for ghostty-renderer-capi.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Contracts;

namespace RoyalTerminal.Rendering.Interop.Native;

internal static unsafe partial class GhosttyRendererNative
{
    public const string LibraryName = "ghostty-renderer-capi";

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_context_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ContextNew();

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_context_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ContextFree(nint context);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SurfaceNew(nint context, RenderBackendKind backend);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceFree(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_set_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetSize(nint surface, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_set_scale")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetScale(nint surface, double scaleX, double scaleY);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_set_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetFocus(nint surface, byte focused);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_set_color_scheme")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetColorScheme(nint surface, uint colorScheme);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_begin_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceBeginFrame(nint surface, out ulong frameToken);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_end_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceEndFrame(nint surface, ulong frameToken);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_validate_target")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceValidateTarget(
        nint surface,
        in GhosttyRenderTargetDescriptorNative target,
        out nint errorUtf8);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_render_to_target")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceRenderToTarget(
        nint surface,
        in GhosttyRenderTargetDescriptorNative target,
        out ulong syncToken);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_surface_render_to_rgba")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceRenderToRgba(
        nint surface,
        nint dstRgba,
        uint dstLength,
        int width,
        int height,
        int stride);

    [LibraryImport(LibraryName, EntryPoint = "ghostty_render_result_message")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint RenderResultMessage(int resultCode);
}
