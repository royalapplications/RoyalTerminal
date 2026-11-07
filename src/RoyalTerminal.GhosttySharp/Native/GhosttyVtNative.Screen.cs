// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttyStyleColorTag : int
    {
        None = 0,
        Palette = 1,
        Rgb = 2,
    }

    [StructLayout(LayoutKind.Explicit, Size = 3)]
    public struct GhosttyStyleColorValue
    {
        [FieldOffset(0)]
        public byte Palette;

        [FieldOffset(0)]
        public GhosttyColorRgb Rgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyStyleColor
    {
        public byte TagValue;

        /// <summary>Gets or sets the byte-sized color discriminator.</summary>
        public GhosttyStyleColorTag Tag
        {
            readonly get => (GhosttyStyleColorTag)TagValue;
            set => TagValue = (byte)value;
        }
        public GhosttyStyleColorValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyStyle
    {
        public GhosttyStyleColor ForegroundColor;
        public GhosttyStyleColor BackgroundColor;
        public GhosttyStyleColor UnderlineColor;
        public ushort Flags;
        public ushort Reserved;

        /// <summary>Gets or sets the bold style flag.</summary>
        public bool Bold
        {
            readonly get => (Flags & 1) != 0;
            set => Flags = (ushort)(value ? Flags | 1 : Flags & ~1);
        }

        /// <summary>Gets or sets the italic style flag.</summary>
        public bool Italic
        {
            readonly get => (Flags & 2) != 0;
            set => Flags = (ushort)(value ? Flags | 2 : Flags & ~2);
        }

        /// <summary>Gets or sets the faint style flag.</summary>
        public bool Faint
        {
            readonly get => (Flags & 4) != 0;
            set => Flags = (ushort)(value ? Flags | 4 : Flags & ~4);
        }

        /// <summary>Gets or sets the blink style flag.</summary>
        public bool Blink
        {
            readonly get => (Flags & 8) != 0;
            set => Flags = (ushort)(value ? Flags | 8 : Flags & ~8);
        }

        /// <summary>Gets or sets the inverse style flag.</summary>
        public bool Inverse
        {
            readonly get => (Flags & 16) != 0;
            set => Flags = (ushort)(value ? Flags | 16 : Flags & ~16);
        }

        /// <summary>Gets or sets the invisible style flag.</summary>
        public bool Invisible
        {
            readonly get => (Flags & 32) != 0;
            set => Flags = (ushort)(value ? Flags | 32 : Flags & ~32);
        }

        /// <summary>Gets or sets the strikethrough style flag.</summary>
        public bool Strikethrough
        {
            readonly get => (Flags & 64) != 0;
            set => Flags = (ushort)(value ? Flags | 64 : Flags & ~64);
        }

        /// <summary>Gets or sets the overline style flag.</summary>
        public bool Overline
        {
            readonly get => (Flags & 128) != 0;
            set => Flags = (ushort)(value ? Flags | 128 : Flags & ~128);
        }

        /// <summary>Gets or sets the three-bit underline style.</summary>
        public int Underline
        {
            readonly get => (Flags >> 8) & 7;
            set => Flags = (ushort)((Flags & ~0x700) | ((value & 7) << 8));
        }

        /// <summary>Creates a zeroed, frozen libghostty 1.0 style value.</summary>
        public static GhosttyStyle CreateSized() => default;
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_style_default")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void StyleDefault(ref GhosttyStyle style);

    [LibraryImport(LibName, EntryPoint = "ghostty_style_is_default")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool StyleIsDefault(in GhosttyStyle style);

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyGridRef
    {
        public nint Node;
        public ushort X;
        public ushort Y;

        public static GhosttyGridRef CreateSized()
        {
            return new GhosttyGridRef
            {

            };
        }
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_grid_ref_cell")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult GridRefCell(in GhosttyGridRef reference, out ulong cell);

    [LibraryImport(LibName, EntryPoint = "ghostty_grid_ref_row")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult GridRefRow(in GhosttyGridRef reference, out ulong row);

    [LibraryImport(LibName, EntryPoint = "ghostty_grid_ref_graphemes")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult GridRefGraphemes(
        in GhosttyGridRef reference,
        uint* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_grid_ref_hyperlink_uri")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult GridRefHyperlinkUri(
        in GhosttyGridRef reference,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_grid_ref_style")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult GridRefStyle(in GhosttyGridRef reference, out GhosttyStyle style);

    public enum GhosttyCellContentTag : int
    {
        Codepoint = 0,
        CodepointGrapheme = 1,
        BackgroundColorPalette = 2,
        BackgroundColorRgb = 3,
    }

    public enum GhosttyCellWide : int
    {
        Narrow = 0,
        Wide = 1,
        SpacerTail = 2,
        SpacerHead = 3,
    }

    public enum GhosttyCellSemanticContent : int
    {
        Output = 0,
        Input = 1,
        Prompt = 2,
    }

    public enum GhosttyCellData : int
    {
        Invalid = 0,
        Codepoint = 1,
        ContentTag = 2,
        Wide = 3,
        HasText = 4,
        HasStyling = 5,
        StyleId = 6,
        HasHyperlink = 7,
        Protected = 8,
        SemanticContent = 9,
        ColorPalette = 10,
        ColorRgb = 11,
    }

    public enum GhosttyRowSemanticPrompt : int
    {
        None = 0,
        Prompt = 1,
        PromptContinuation = 2,
    }

    public enum GhosttyRowData : int
    {
        Invalid = 0,
        Wrap = 1,
        WrapContinuation = 2,
        Grapheme = 3,
        Styled = 4,
        Hyperlink = 5,
        SemanticPrompt = 6,
        KittyVirtualPlaceholder = 7,
        Dirty = 8,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_cell_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult CellGet(ulong cell, GhosttyCellData data, void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_cell_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult CellGetMulti(
        ulong cell,
        nuint count,
        GhosttyCellData* keys,
        void** values,
        nuint* outWritten);

    [LibraryImport(LibName, EntryPoint = "ghostty_row_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RowGet(ulong row, GhosttyRowData data, void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_row_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RowGetMulti(
        ulong row,
        nuint count,
        GhosttyRowData* keys,
        void** values,
        nuint* outWritten);
}
