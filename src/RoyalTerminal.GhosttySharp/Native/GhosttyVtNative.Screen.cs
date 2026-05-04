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

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct GhosttyStyleColorValue
    {
        [FieldOffset(0)]
        public byte Palette;

        [FieldOffset(0)]
        public GhosttyColorRgb Rgb;

        [FieldOffset(0)]
        public ulong Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyStyleColor
    {
        public GhosttyStyleColorTag Tag;
        public GhosttyStyleColorValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyStyle
    {
        public nuint Size;
        public GhosttyStyleColor ForegroundColor;
        public GhosttyStyleColor BackgroundColor;
        public GhosttyStyleColor UnderlineColor;

        [MarshalAs(UnmanagedType.U1)]
        public bool Bold;

        [MarshalAs(UnmanagedType.U1)]
        public bool Italic;

        [MarshalAs(UnmanagedType.U1)]
        public bool Faint;

        [MarshalAs(UnmanagedType.U1)]
        public bool Blink;

        [MarshalAs(UnmanagedType.U1)]
        public bool Inverse;

        [MarshalAs(UnmanagedType.U1)]
        public bool Invisible;

        [MarshalAs(UnmanagedType.U1)]
        public bool Strikethrough;

        [MarshalAs(UnmanagedType.U1)]
        public bool Overline;

        public int Underline;

        public static GhosttyStyle CreateSized()
        {
            return new GhosttyStyle
            {
                Size = (nuint)Marshal.SizeOf<GhosttyStyle>(),
            };
        }
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
        public nuint Size;
        public nint Node;
        public ushort X;
        public ushort Y;

        public static GhosttyGridRef CreateSized()
        {
            return new GhosttyGridRef
            {
                Size = (nuint)Marshal.SizeOf<GhosttyGridRef>(),
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
