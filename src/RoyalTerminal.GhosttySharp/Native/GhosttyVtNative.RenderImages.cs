// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Protocol owning an image's ID and pixels. Skip unknown protocols.</summary>
    public enum GhosttyImageProtocol : int
    {
        /// <summary>Kitty graphics protocol.</summary>
        Kitty = 0,
    }

    /// <summary>Drawing layer, ordered from lowest to highest.</summary>
    public enum GhosttyRenderStateImageLayer : int
    {
        /// <summary>Behind cell backgrounds.</summary>
        BelowBackground = 0,
        /// <summary>Between backgrounds and text.</summary>
        BelowText = 1,
        /// <summary>In front of text.</summary>
        AboveText = 2,
    }

    /// <summary>Origin of a Kitty render placement.</summary>
    public enum GhosttyRenderStateKittyPlacementKind : int
    {
        /// <summary>One row of adjacent Unicode placeholder cells.</summary>
        Virtual = 0,
        /// <summary>Positioned relative to a parent placement.</summary>
        Relative = 1,
        /// <summary>Pinned to a screen cell.</summary>
        Fixed = 2,
    }

    /// <summary>Fields available from the current render-image iterator entry.</summary>
    public enum GhosttyRenderStateImageData : int
    {
        /// <summary>Invalid query.</summary>
        Invalid = 0,
        /// <summary>Image protocol (GhosttyImageProtocol).</summary>
        Protocol = 1,
        /// <summary>Protocol-specific image ID (uint32).</summary>
        ImageId = 2,
        /// <summary>Image content generation (uint64).</summary>
        ImageGeneration = 3,
        /// <summary>Drawing layer (GhosttyRenderStateImageLayer).</summary>
        Layer = 4,
        /// <summary>Z-index within the layer (int32).</summary>
        Z = 5,
        /// <summary>Signed viewport column (int32).</summary>
        ViewportColumn = 6,
        /// <summary>Signed viewport row (int32).</summary>
        ViewportRow = 7,
        /// <summary>Occupied columns (uint32).</summary>
        GridColumns = 8,
        /// <summary>Occupied rows (uint32).</summary>
        GridRows = 9,
        /// <summary>Horizontal pixel offset inside the anchor cell (uint32).</summary>
        CellOffsetX = 10,
        /// <summary>Vertical pixel offset inside the anchor cell (uint32).</summary>
        CellOffsetY = 11,
        /// <summary>Destination width in pixels (uint32).</summary>
        PixelWidth = 12,
        /// <summary>Destination height in pixels (uint32).</summary>
        PixelHeight = 13,
        /// <summary>Source rectangle x in image pixels (uint32).</summary>
        SourceX = 14,
        /// <summary>Source rectangle y in image pixels (uint32).</summary>
        SourceY = 15,
        /// <summary>Source rectangle width (uint32).</summary>
        SourceWidth = 16,
        /// <summary>Source rectangle height (uint32).</summary>
        SourceHeight = 17,
        /// <summary>Kitty placement ID (uint32), or NoValue for other protocols.</summary>
        KittyPlacementId = 18,
        /// <summary>Kitty placement kind, or NoValue for other protocols.</summary>
        KittyKind = 19,
    }

    /// <summary>Allocates a reusable render-image iterator.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_image_iterator_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateImageIteratorNew(nint allocator, out nint iterator);

    /// <summary>Frees a render-image iterator.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_image_iterator_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void RenderStateImageIteratorFree(nint iterator);

    /// <summary>Selects the next image placement.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_image_iterator_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RenderStateImageIteratorNext(nint iterator);

    /// <summary>Reads one field of the current placement.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_image_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateImageGet(
        nint iterator, GhosttyRenderStateImageData data, void* output);

    /// <summary>Reads multiple fields; written counts the successful prefix.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_image_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateImageGetMulti(
        nint iterator, nuint count, GhosttyRenderStateImageData* keys, void** values, nuint* written);
}
