// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyColorPaletteMask
    {
        private fixed ulong _bits[4];

        public void Set(byte index)
        {
            fixed (ulong* bits = _bits)
            {
                bits[index >> 6] |= 1UL << (index & 63);
            }
        }

        public void Clear(byte index)
        {
            fixed (ulong* bits = _bits)
            {
                bits[index >> 6] &= ~(1UL << (index & 63));
            }
        }

        public readonly bool IsSet(byte index)
        {
            fixed (ulong* bits = _bits)
            {
                return (bits[index >> 6] & (1UL << (index & 63))) != 0;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyColorX11Entry
    {
        public nint Name;
        public GhosttyColorRgb Color;
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_color_parse_x11")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult ColorParseX11(
        byte* name,
        nuint length,
        out GhosttyColorRgb color);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_parse")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult ColorParse(
        byte* value,
        nuint length,
        out GhosttyColorRgb color);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_parse_palette_entry")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult ColorParsePaletteEntry(
        byte* value,
        nuint length,
        out byte index,
        out GhosttyColorRgb color);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_palette_default")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void ColorPaletteDefault(GhosttyColorRgb* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_palette_generate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void ColorPaletteGenerate(
        GhosttyColorRgb* basePalette,
        GhosttyColorPaletteMask* skip,
        in GhosttyColorRgb background,
        in GhosttyColorRgb foreground,
        [MarshalAs(UnmanagedType.U1)] bool harmonious,
        GhosttyColorRgb* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_luminance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial double ColorLuminance(in GhosttyColorRgb color);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_perceived_luminance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial double ColorPerceivedLuminance(in GhosttyColorRgb color);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_contrast")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial double ColorContrast(
        in GhosttyColorRgb first,
        in GhosttyColorRgb second);

    [LibraryImport(LibName, EntryPoint = "ghostty_color_x11_names")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint ColorX11Names();

    [LibraryImport(LibName, EntryPoint = "ghostty_color_x11_name_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nuint ColorX11NameCount();

    [LibraryImport(LibName, EntryPoint = "ghostty_color_scheme_report_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult ColorSchemeReportEncode(
        GhosttyColorScheme scheme,
        byte* buffer,
        nuint bufferLength,
        out nuint written);
}
