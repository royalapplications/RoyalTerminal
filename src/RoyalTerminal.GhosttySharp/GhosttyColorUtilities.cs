// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// One entry from Ghostty's process-lifetime X11 color-name table.
/// </summary>
/// <param name="Name">The exact X11 color name.</param>
/// <param name="Color">The corresponding RGB value.</param>
public readonly record struct GhosttyX11Color(string Name, GhosttyVtNative.GhosttyColorRgb Color);

/// <summary>
/// Managed access to Ghostty color parsing, palette generation, and color math.
/// </summary>
public static class GhosttyColorUtilities
{
    /// <summary>Attempts to parse any color syntax accepted by Ghostty.</summary>
    public static unsafe bool TryParse(
        string value,
        out GhosttyVtNative.GhosttyColorRgb color)
    {
        ArgumentNullException.ThrowIfNull(value);
        NativeLibraryLoader.Initialize();
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        fixed (byte* valuePtr = utf8)
        {
            return GhosttyVtNative.ColorParse(valuePtr, (nuint)utf8.Length, out color) ==
                   GhosttyVtNative.GhosttyResult.Success;
        }
    }

    /// <summary>Attempts to parse an X11 color name using Ghostty's embedded table.</summary>
    public static unsafe bool TryParseX11(
        string name,
        out GhosttyVtNative.GhosttyColorRgb color)
    {
        ArgumentNullException.ThrowIfNull(name);
        NativeLibraryLoader.Initialize();
        byte[] utf8 = Encoding.UTF8.GetBytes(name);
        fixed (byte* namePtr = utf8)
        {
            return GhosttyVtNative.ColorParseX11(namePtr, (nuint)utf8.Length, out color) ==
                   GhosttyVtNative.GhosttyResult.Success;
        }
    }

    /// <summary>Attempts to parse an <c>INDEX=COLOR</c> Ghostty palette entry.</summary>
    public static unsafe bool TryParsePaletteEntry(
        string value,
        out byte index,
        out GhosttyVtNative.GhosttyColorRgb color)
    {
        ArgumentNullException.ThrowIfNull(value);
        NativeLibraryLoader.Initialize();
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        fixed (byte* valuePtr = utf8)
        {
            return GhosttyVtNative.ColorParsePaletteEntry(
                       valuePtr,
                       (nuint)utf8.Length,
                       out index,
                       out color) ==
                   GhosttyVtNative.GhosttyResult.Success;
        }
    }

    /// <summary>Creates Ghostty's built-in 256-color palette.</summary>
    public static unsafe GhosttyVtNative.GhosttyColorRgb[] CreateDefaultPalette()
    {
        NativeLibraryLoader.Initialize();
        GhosttyVtNative.GhosttyColorRgb[] result = new GhosttyVtNative.GhosttyColorRgb[256];
        fixed (GhosttyVtNative.GhosttyColorRgb* resultPtr = result)
        {
            GhosttyVtNative.ColorPaletteDefault(resultPtr);
        }

        return result;
    }

    /// <summary>
    /// Generates Ghostty's color cube and grayscale ramp from a 256-color base palette.
    /// </summary>
    public static unsafe GhosttyVtNative.GhosttyColorRgb[] GeneratePalette(
        ReadOnlySpan<GhosttyVtNative.GhosttyColorRgb> basePalette,
        in GhosttyVtNative.GhosttyColorPaletteMask skip,
        GhosttyVtNative.GhosttyColorRgb background,
        GhosttyVtNative.GhosttyColorRgb foreground,
        bool harmonious)
    {
        if (basePalette.Length != 256)
        {
            throw new ArgumentException("Base palette must contain exactly 256 colors.", nameof(basePalette));
        }

        NativeLibraryLoader.Initialize();
        GhosttyVtNative.GhosttyColorRgb[] result = new GhosttyVtNative.GhosttyColorRgb[256];
        GhosttyVtNative.GhosttyColorPaletteMask skipCopy = skip;
        fixed (GhosttyVtNative.GhosttyColorRgb* basePtr = basePalette)
        fixed (GhosttyVtNative.GhosttyColorRgb* resultPtr = result)
        {
            GhosttyVtNative.ColorPaletteGenerate(
                basePtr,
                &skipCopy,
                in background,
                in foreground,
                harmonious,
                resultPtr);
        }

        return result;
    }

    /// <summary>Calculates W3C relative luminance in the range 0 through 1.</summary>
    public static double GetLuminance(GhosttyVtNative.GhosttyColorRgb color)
    {
        NativeLibraryLoader.Initialize();
        return GhosttyVtNative.ColorLuminance(in color);
    }

    /// <summary>Calculates Ghostty's perceived luminance in the range 0 through 1.</summary>
    public static double GetPerceivedLuminance(GhosttyVtNative.GhosttyColorRgb color)
    {
        NativeLibraryLoader.Initialize();
        return GhosttyVtNative.ColorPerceivedLuminance(in color);
    }

    /// <summary>Calculates the WCAG contrast ratio between two colors.</summary>
    public static double GetContrast(
        GhosttyVtNative.GhosttyColorRgb first,
        GhosttyVtNative.GhosttyColorRgb second)
    {
        NativeLibraryLoader.Initialize();
        return GhosttyVtNative.ColorContrast(in first, in second);
    }

    /// <summary>Copies Ghostty's process-lifetime X11 color-name table.</summary>
    public static unsafe GhosttyX11Color[] GetX11Colors()
    {
        NativeLibraryLoader.Initialize();
        nuint count = GhosttyVtNative.ColorX11NameCount();
        GhosttyVtNative.GhosttyColorX11Entry* entries =
            (GhosttyVtNative.GhosttyColorX11Entry*)GhosttyVtNative.ColorX11Names();
        GhosttyX11Color[] result = new GhosttyX11Color[checked((int)count)];

        for (int index = 0; index < result.Length; index++)
        {
            GhosttyVtNative.GhosttyColorX11Entry entry = entries[index];
            string name = Marshal.PtrToStringUTF8(entry.Name) ?? string.Empty;
            result[index] = new GhosttyX11Color(name, entry.Color);
        }

        return result;
    }

    /// <summary>Encodes a mode-2031 color-scheme report for the PTY.</summary>
    public static unsafe byte[] EncodeColorSchemeReport(GhosttyColorScheme scheme)
    {
        NativeLibraryLoader.Initialize();
        GhosttyVtNative.GhosttyResult probe =
            GhosttyVtNative.ColorSchemeReportEncode(scheme, null, 0, out nuint required);
        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_color_scheme_report_encode(probe)");
        }

        byte[] result = new byte[checked((int)required)];
        fixed (byte* resultPtr = result)
        {
            ThrowIfFailed(
                GhosttyVtNative.ColorSchemeReportEncode(
                    scheme,
                    resultPtr,
                    (nuint)result.Length,
                    out nuint written),
                "ghostty_color_scheme_report_encode");
            if (written != (nuint)result.Length)
            {
                Array.Resize(ref result, checked((int)written));
            }
        }

        return result;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }
}
