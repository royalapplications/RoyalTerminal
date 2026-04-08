// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttyKittyGraphicsData : int
    {
        Invalid = 0,
        PlacementIterator = 1,
    }

    public enum GhosttyKittyGraphicsPlacementData : int
    {
        Invalid = 0,
        ImageId = 1,
        PlacementId = 2,
        IsVirtual = 3,
        XOffset = 4,
        YOffset = 5,
        SourceX = 6,
        SourceY = 7,
        SourceWidth = 8,
        SourceHeight = 9,
        Columns = 10,
        Rows = 11,
        Z = 12,
    }

    public enum GhosttyKittyPlacementLayer : int
    {
        All = 0,
        BelowBackground = 1,
        BelowText = 2,
        AboveText = 3,
    }

    public enum GhosttyKittyGraphicsPlacementIteratorOption : int
    {
        Layer = 0,
    }

    public enum GhosttyKittyImageFormat : int
    {
        Rgb = 0,
        Rgba = 1,
        Png = 2,
        GrayAlpha = 3,
        Gray = 4,
    }

    public enum GhosttyKittyImageCompression : int
    {
        None = 0,
        ZlibDeflate = 1,
    }

    public enum GhosttyKittyGraphicsImageData : int
    {
        Invalid = 0,
        Id = 1,
        Number = 2,
        Width = 3,
        Height = 4,
        Format = 5,
        Compression = 6,
        DataPtr = 7,
        DataLength = 8,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsGet(
        nint graphics,
        GhosttyKittyGraphicsData data,
        void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_image")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint KittyGraphicsImage(nint graphics, uint imageId);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_image_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsImageGet(
        nint image,
        GhosttyKittyGraphicsImageData data,
        void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_iterator_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KittyGraphicsPlacementIteratorNew(nint allocator, out nint iterator);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_iterator_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void KittyGraphicsPlacementIteratorFree(nint iterator);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_iterator_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementIteratorSet(
        nint iterator,
        GhosttyKittyGraphicsPlacementIteratorOption option,
        void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool KittyGraphicsPlacementNext(nint iterator);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementGet(
        nint iterator,
        GhosttyKittyGraphicsPlacementData data,
        void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_rect")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementRect(
        nint iterator,
        nint image,
        nint terminal,
        GhosttySelectionRange* selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_pixel_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementPixelSize(
        nint iterator,
        nint image,
        nint terminal,
        uint* width,
        uint* height);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_grid_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementGridSize(
        nint iterator,
        nint image,
        nint terminal,
        uint* columns,
        uint* rows);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_viewport_pos")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementViewportPosition(
        nint iterator,
        nint image,
        nint terminal,
        int* column,
        int* row);

    [LibraryImport(LibName, EntryPoint = "ghostty_kitty_graphics_placement_source_rect")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementSourceRect(
        nint iterator,
        nint image,
        uint* x,
        uint* y,
        uint* width,
        uint* height);
}
