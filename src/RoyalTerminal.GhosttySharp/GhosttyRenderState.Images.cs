// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.GhosttySharp;

/// <summary>Owned value describing one image rectangle in a render-state snapshot.</summary>
public struct GhosttyRenderImagePlacement
{
    /// <summary>Protocol that owns ImageId. Skip unsupported protocols.</summary>
    public GhosttyImageProtocol Protocol;
    /// <summary>Protocol-specific image identifier.</summary>
    public uint ImageId;
    /// <summary>Image contents generation; compare for equality only.</summary>
    public ulong ImageGeneration;
    /// <summary>Drawing layer relative to backgrounds and text.</summary>
    public GhosttyRenderStateImageLayer Layer;
    /// <summary>Z-index within the layer.</summary>
    public int Z;
    /// <summary>Signed cell column relative to the viewport.</summary>
    public int ViewportColumn;
    /// <summary>Signed cell row relative to the viewport.</summary>
    public int ViewportRow;
    /// <summary>Number of cell columns occupied.</summary>
    public uint GridColumns;
    /// <summary>Number of cell rows occupied.</summary>
    public uint GridRows;
    /// <summary>Horizontal pixel offset from the anchor cell.</summary>
    public uint CellOffsetX;
    /// <summary>Vertical pixel offset from the anchor cell.</summary>
    public uint CellOffsetY;
    /// <summary>Destination width in pixels.</summary>
    public uint PixelWidth;
    /// <summary>Destination height in pixels.</summary>
    public uint PixelHeight;
    /// <summary>Source rectangle left in image pixels.</summary>
    public uint SourceX;
    /// <summary>Source rectangle top in image pixels.</summary>
    public uint SourceY;
    /// <summary>Source rectangle width in image pixels.</summary>
    public uint SourceWidth;
    /// <summary>Source rectangle height in image pixels.</summary>
    public uint SourceHeight;
    /// <summary>Kitty placement identifier, or null for another protocol.</summary>
    public uint? KittyPlacementId;
    /// <summary>Kitty placement origin, or null for another protocol.</summary>
    public GhosttyRenderStateKittyPlacementKind? KittyKind;
}

public sealed partial class GhosttyRenderState
{
    private nint _imageIterator;

    /// <summary>Gets the equality-only generation of the entire image placement list.</summary>
    public ulong GetImagePlacementsGeneration()
        => GetValue<ulong>(GhosttyRenderStateData.ImagePlacementsGeneration);

    /// <summary>
    /// Populates the reusable image iterator from the last update. Reinitialize after
    /// each update. Entries include fixed, relative and placeholder images; drawing
    /// those again from the Kitty storage iterator would duplicate them.
    /// </summary>
    public unsafe void ResetImageIterator()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_imageIterator == nint.Zero)
            ThrowIfFailed(RenderStateImageIteratorNew(0, out _imageIterator), "ghostty_render_state_image_iterator_new");
        nint iterator = _imageIterator;
        ThrowIfFailed(RenderStateGet(_handle, GhosttyRenderStateData.ImageIterator, &iterator),
            "ghostty_render_state_get(image_iterator)");
    }

    /// <summary>Advances to the next image; requires ResetImageIterator after each update.</summary>
    public bool MoveNextImage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _imageIterator != nint.Zero && RenderStateImageIteratorNext(_imageIterator);
    }

    /// <summary>
    /// Copies the current placement geometry without copying image pixels. Clip its
    /// full rectangle to the rendered viewport/overscan area and sort by layer and Z.
    /// </summary>
    public unsafe GhosttyRenderImagePlacement GetCurrentImage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_imageIterator == nint.Zero) throw new InvalidOperationException("Initialize and advance the image iterator first.");
        GhosttyRenderImagePlacement value = default;
        GhosttyRenderStateImageData* keys = stackalloc GhosttyRenderStateImageData[]
        {
            GhosttyRenderStateImageData.Protocol, GhosttyRenderStateImageData.ImageId,
            GhosttyRenderStateImageData.ImageGeneration, GhosttyRenderStateImageData.Layer,
            GhosttyRenderStateImageData.Z, GhosttyRenderStateImageData.ViewportColumn,
            GhosttyRenderStateImageData.ViewportRow, GhosttyRenderStateImageData.GridColumns,
            GhosttyRenderStateImageData.GridRows, GhosttyRenderStateImageData.CellOffsetX,
            GhosttyRenderStateImageData.CellOffsetY, GhosttyRenderStateImageData.PixelWidth,
            GhosttyRenderStateImageData.PixelHeight, GhosttyRenderStateImageData.SourceX,
            GhosttyRenderStateImageData.SourceY, GhosttyRenderStateImageData.SourceWidth,
            GhosttyRenderStateImageData.SourceHeight,
        };
        void** outputs = stackalloc void*[]
        {
            &value.Protocol, &value.ImageId, &value.ImageGeneration, &value.Layer, &value.Z,
            &value.ViewportColumn, &value.ViewportRow, &value.GridColumns, &value.GridRows,
            &value.CellOffsetX, &value.CellOffsetY, &value.PixelWidth, &value.PixelHeight,
            &value.SourceX, &value.SourceY, &value.SourceWidth, &value.SourceHeight,
        };
        ThrowIfFailed(RenderStateImageGetMulti(_imageIterator, 17, keys, outputs, null),
            "ghostty_render_state_image_get_multi");
        if (value.Protocol == GhosttyImageProtocol.Kitty)
        {
            uint placementId = 0;
            GhosttyRenderStateKittyPlacementKind kind = default;
            ThrowIfFailed(RenderStateImageGet(_imageIterator, GhosttyRenderStateImageData.KittyPlacementId, &placementId),
                "ghostty_render_state_image_get(kitty_placement_id)");
            ThrowIfFailed(RenderStateImageGet(_imageIterator, GhosttyRenderStateImageData.KittyKind, &kind),
                "ghostty_render_state_image_get(kitty_kind)");
            value.KittyPlacementId = placementId;
            value.KittyKind = kind;
        }
        return value;
    }
}
