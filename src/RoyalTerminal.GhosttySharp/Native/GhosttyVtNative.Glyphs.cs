// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Owned-copy metadata for a FIFO-ordered glyph entry. Contains no borrowed pointers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoyalGlyphMetadata
    {
        /// <summary>Size of this structure.</summary>
        public nuint Size;
        /// <summary>Registered private-use codepoint.</summary>
        public uint Codepoint;
        /// <summary>Positive design units per em.</summary>
        public uint UnitsPerEm;
        /// <summary>Positive authored advance width.</summary>
        public uint AdvanceWidth;
        /// <summary>Positive authored line height.</summary>
        public uint LineHeight;
        /// <summary>Requested cell width: one or two.</summary>
        public uint Width;
        /// <summary>Normalized sizing: zero unchanged, one aspect-fit, two stretch.</summary>
        public uint Sizing;
        /// <summary>Horizontal alignment: zero start, one center, two end.</summary>
        public uint Horizontal;
        /// <summary>Vertical alignment: zero start, one center, two end.</summary>
        public uint Vertical;
        /// <summary>Required contour endpoint count.</summary>
        public uint ContourCount;
        /// <summary>Required point count.</summary>
        public uint PointCount;
        /// <summary>Top fractional padding.</summary>
        public double PadTop;
        /// <summary>Right fractional padding.</summary>
        public double PadRight;
        /// <summary>Bottom fractional padding.</summary>
        public double PadBottom;
        /// <summary>Left fractional padding.</summary>
        public double PadLeft;

        /// <summary>Initializes the required ABI size.</summary>
        public static RoyalGlyphMetadata CreateSized() => new() { Size = (nuint)Unsafe.SizeOf<RoyalGlyphMetadata>() };
    }

    /// <summary>ABI-stable glyph point in Y-up design coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoyalGlyphPoint
    {
        /// <summary>Horizontal coordinate.</summary>
        public int X;
        /// <summary>Vertical coordinate.</summary>
        public int Y;
        /// <summary>Exactly zero for a control point or one for an on-curve point.</summary>
        public uint OnCurve;
    }

    /// <summary>Reads count and dirty flag without clearing it; serialize with terminal access.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_glyph_glossary_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult GlyphGlossaryInfo(nint terminal, uint* count, byte* dirty);

    /// <summary>Copies one FIFO-ordered entry's normalized metrics and constraints.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_glyph_metadata")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult GlyphMetadata(nint terminal, uint index, RoyalGlyphMetadata* metadata);

    /// <summary>Copies an outline without exposing native storage. Insufficient capacity writes neither buffer.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_glyph_outline")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult GlyphOutline(nint terminal, uint index,
        RoyalGlyphPoint* points, nuint pointCapacity, ushort* contours, nuint contourCapacity);
}
