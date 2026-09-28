// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Location/key attributes returned by RoyalTerminal's placement extension.</summary>
    [Flags]
    public enum RoyalKittyPlacementFlags : uint
    {
        /// <summary>An ordinary external placement with no virtual root.</summary>
        None = 0,
        /// <summary>The placement ID belongs to the internally allocated namespace.</summary>
        InternalId = 1,
        /// <summary>The placement is virtual.</summary>
        Virtual = 2,
        /// <summary>The placement's relative chain resolves to a virtual root.</summary>
        VirtualRoot = 4,
    }

    /// <summary>Copied placement key and resolved virtual-root metadata; contains no borrowed pointers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoyalKittyPlacementMetadata
    {
        /// <summary>Size of this structure in bytes.</summary>
        public nuint Size;
        /// <summary>Image ID of the current placement.</summary>
        public uint ImageId;
        /// <summary>Placement ID; its namespace is given by Flags.</summary>
        public uint PlacementId;
        /// <summary>Location and namespace flags.</summary>
        public RoyalKittyPlacementFlags Flags;
        /// <summary>Virtual root image ID, valid only with VirtualRoot.</summary>
        public uint RootImageId;
        /// <summary>Virtual root placement ID, valid only with VirtualRoot.</summary>
        public uint RootPlacementId;
        /// <summary>One if the root ID is internal, zero if external.</summary>
        public uint RootInternal;
        /// <summary>Saturating accumulated horizontal cell offset.</summary>
        public int HorizontalOffset;
        /// <summary>Saturating accumulated vertical cell offset.</summary>
        public int VerticalOffset;

        /// <summary>Initializes the required size field.</summary>
        public static RoyalKittyPlacementMetadata CreateSized()
            => new() { Size = (nuint)Unsafe.SizeOf<RoyalKittyPlacementMetadata>() };
    }

    /// <summary>Reads exact placement namespaces and the upstream-resolved virtual root.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_kitty_graphics_placement_metadata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KittyGraphicsPlacementMetadata(
        nint graphics, nint iterator, RoyalKittyPlacementMetadata* metadata);

    /// <summary>
    /// Advances Kitty animation playback using a monotonic millisecond clock.
    /// RoyalTerminal extension, compiled into the same library as upstream VT.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_kitty_graphics_animation_tick")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KittyGraphicsAnimationTick(
        nint graphics,
        ulong nowMilliseconds,
        out ulong nextDelayMilliseconds);
}
