// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Contracts;

namespace RoyalTerminal.Rendering.Interop.Swift.Native;

[StructLayout(LayoutKind.Sequential)]
public struct SwiftRenderThemeNative
{
    public uint DefaultForegroundOriginal;
    public uint DefaultBackgroundOriginal;
    public uint CursorOriginal;
}

[StructLayout(LayoutKind.Sequential)]
public struct SwiftTerminalCellNative
{
    public int Codepoint;
    public uint Foreground;
    public uint Background;
    public byte Attributes;
    public byte UnderlineStyle;
    public byte Decorations;
    public byte Width;
}

[StructLayout(LayoutKind.Sequential)]
public struct SwiftRenderTargetDescriptorNative
{
    public RenderBackendKind Backend;
    public RenderTargetKind TargetKind;
    public RenderPixelFormat PixelFormat;

    public int Width;
    public int Height;
    public uint SampleCount;

    public nint DeviceHandle;
    public nint ContextHandle;
    public nint CommandQueueHandle;
    public nint CommandBufferHandle;
    public nint TargetHandle;
    public nint TargetViewHandle;

    public ulong FrameId;
    public nint DebugNameUtf8;
}

internal static unsafe partial class SwiftRendererNative
{
    public const string LibraryName = "swift_terminal_renderer";

    [LibraryImport(LibraryName, EntryPoint = "swift_render_context_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ContextNew();

    [LibraryImport(LibraryName, EntryPoint = "swift_render_context_new_with_device")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ContextNewWithDevice(nint deviceHandle);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_create_texture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint CreateTexture(nint deviceHandle, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_free_texture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void FreeTexture(nint textureHandle);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_context_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void ContextFree(nint context);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SurfaceNew(nint context, int backend);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SurfaceFree(nint surface);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_set_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetSize(nint surface, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_set_scale")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetScale(nint surface, double scaleX, double scaleY);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_set_theme")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetTheme(nint surface, in SwiftRenderThemeNative theme);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_set_focus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetFocus(nint surface, byte focused);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_set_font")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetFont(nint surface, [MarshalAs(UnmanagedType.LPStr)] string fontName, double fontSize);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_set_cell_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceSetCellSize(nint surface, double cellWidth, double cellHeight, double baseline);

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_render_to_target")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceRenderToTarget(
        nint surface,
        in SwiftRenderTargetDescriptorNative target,
        SwiftTerminalCellNative* flatCells,
        int cellCount,
        int cols,
        int rows,
        int cursorCol,
        int cursorRow,
        byte cursorVisible,
        int cursorStyle
    );

    [LibraryImport(LibraryName, EntryPoint = "swift_render_surface_render_to_rgba")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SurfaceRenderToRgba(
        nint surface,
        nint dstRgba,
        uint dstLength,
        int width,
        int height,
        int stride,
        SwiftTerminalCellNative* flatCells,
        int cellCount,
        int cols,
        int rows,
        int cursorCol,
        int cursorRow,
        byte cursorVisible,
        int cursorStyle
    );
}
