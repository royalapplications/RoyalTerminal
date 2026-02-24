// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Generates 256-color palettes from ANSI base colors.
/// </summary>
public static class TerminalPaletteGenerator
{
    private const int PaletteSize = 256;
    private static readonly byte[] s_cubeLevels = [0, 95, 135, 175, 215, 255];

    /// <summary>
    /// Generates a full 256-color palette from ANSI base colors.
    /// </summary>
    public static uint[] Generate(ReadOnlySpan<uint> base16, TerminalPaletteGenerationMode mode)
    {
        Span<uint> normalizedBase = stackalloc uint[16];
        NormalizeBase16(base16, normalizedBase);

        uint[] result = new uint[PaletteSize];
        for (int i = 0; i < 16; i++)
        {
            result[i] = normalizedBase[i];
        }

        switch (mode)
        {
            case TerminalPaletteGenerationMode.Canonical:
                FillCanonicalCubeAndGray(result);
                break;

            case TerminalPaletteGenerationMode.DerivedFromBase16Lab:
                FillLabDerivedCubeAndGray(result, normalizedBase);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown palette generation mode.");
        }

        return result;
    }

    private static void FillCanonicalCubeAndGray(uint[] target)
    {
        int index = 16;
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    target[index++] = PackArgb(s_cubeLevels[r], s_cubeLevels[g], s_cubeLevels[b]);
                }
            }
        }

        for (int i = 0; i < 24; i++)
        {
            byte gray = (byte)(8 + (10 * i));
            target[232 + i] = PackArgb(gray, gray, gray);
        }
    }

    private static void FillLabDerivedCubeAndGray(uint[] target, ReadOnlySpan<uint> base16)
    {
        // Use classic RGB cube corners mapped to ANSI semantic colors.
        LabColor c000 = ToLab(base16[0]);   // black
        LabColor c100 = ToLab(base16[1]);   // red
        LabColor c010 = ToLab(base16[2]);   // green
        LabColor c110 = ToLab(base16[3]);   // yellow
        LabColor c001 = ToLab(base16[4]);   // blue
        LabColor c101 = ToLab(base16[5]);   // magenta
        LabColor c011 = ToLab(base16[6]);   // cyan
        LabColor c111 = ToLab(base16[15]);  // bright white

        int index = 16;
        for (int r = 0; r < 6; r++)
        {
            double tr = r / 5.0;
            for (int g = 0; g < 6; g++)
            {
                double tg = g / 5.0;
                for (int b = 0; b < 6; b++)
                {
                    double tb = b / 5.0;
                    LabColor lab = Trilinear(
                        c000, c100, c010, c110,
                        c001, c101, c011, c111,
                        tr, tg, tb);
                    target[index++] = FromLab(lab);
                }
            }
        }

        LabColor grayStart = ToLab(base16[0]);
        LabColor grayEnd = ToLab(base16[15]);
        for (int i = 0; i < 24; i++)
        {
            // Match xterm grayscale indexing while deriving tone from base colors.
            double t = (i + 1) / 25.0;
            LabColor mix = Lerp(grayStart, grayEnd, t);
            target[232 + i] = FromLab(mix);
        }
    }

    private static void NormalizeBase16(ReadOnlySpan<uint> source, Span<uint> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = i < source.Length
                ? EnsureOpaque(source[i])
                : TerminalThemeDefaults.Base16Canonical[i];
        }
    }

    private static uint EnsureOpaque(uint argb)
    {
        return (argb & 0x00FFFFFFu) | 0xFF000000u;
    }

    private static uint PackArgb(byte r, byte g, byte b)
    {
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static LabColor Trilinear(
        LabColor c000,
        LabColor c100,
        LabColor c010,
        LabColor c110,
        LabColor c001,
        LabColor c101,
        LabColor c011,
        LabColor c111,
        double tx,
        double ty,
        double tz)
    {
        LabColor x00 = Lerp(c000, c100, tx);
        LabColor x10 = Lerp(c010, c110, tx);
        LabColor x01 = Lerp(c001, c101, tx);
        LabColor x11 = Lerp(c011, c111, tx);

        LabColor y0 = Lerp(x00, x10, ty);
        LabColor y1 = Lerp(x01, x11, ty);

        return Lerp(y0, y1, tz);
    }

    private static LabColor Lerp(LabColor a, LabColor b, double t)
    {
        return new LabColor(
            a.L + ((b.L - a.L) * t),
            a.A + ((b.A - a.A) * t),
            a.B + ((b.B - a.B) * t));
    }

    private static LabColor ToLab(uint argb)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0;
        double g = ((argb >> 8) & 0xFF) / 255.0;
        double b = (argb & 0xFF) / 255.0;

        r = ToLinearSrgb(r);
        g = ToLinearSrgb(g);
        b = ToLinearSrgb(b);

        // D65/2°
        double x = (r * 0.4124564) + (g * 0.3575761) + (b * 0.1804375);
        double y = (r * 0.2126729) + (g * 0.7151522) + (b * 0.0721750);
        double z = (r * 0.0193339) + (g * 0.1191920) + (b * 0.9503041);

        const double xn = 0.95047;
        const double yn = 1.00000;
        const double zn = 1.08883;

        double fx = PivotLab(x / xn);
        double fy = PivotLab(y / yn);
        double fz = PivotLab(z / zn);

        double l = Math.Max(0.0, (116.0 * fy) - 16.0);
        double a = 500.0 * (fx - fy);
        double bLab = 200.0 * (fy - fz);
        return new LabColor(l, a, bLab);
    }

    private static uint FromLab(LabColor lab)
    {
        const double xn = 0.95047;
        const double yn = 1.00000;
        const double zn = 1.08883;

        double fy = (lab.L + 16.0) / 116.0;
        double fx = fy + (lab.A / 500.0);
        double fz = fy - (lab.B / 200.0);

        double xr = InversePivotLab(fx);
        double yr = InversePivotLab(fy);
        double zr = InversePivotLab(fz);

        double x = xr * xn;
        double y = yr * yn;
        double z = zr * zn;

        double r = (x * 3.2404542) + (y * -1.5371385) + (z * -0.4985314);
        double g = (x * -0.9692660) + (y * 1.8760108) + (z * 0.0415560);
        double b = (x * 0.0556434) + (y * -0.2040259) + (z * 1.0572252);

        byte r8 = ToSrgbByte(r);
        byte g8 = ToSrgbByte(g);
        byte b8 = ToSrgbByte(b);

        return PackArgb(r8, g8, b8);
    }

    private static double ToLinearSrgb(double v)
    {
        if (v <= 0.04045)
        {
            return v / 12.92;
        }

        return Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static byte ToSrgbByte(double linear)
    {
        linear = Math.Clamp(linear, 0.0, 1.0);
        double srgb = linear <= 0.0031308
            ? 12.92 * linear
            : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;

        int value = (int)Math.Round(srgb * 255.0, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(value, 0, 255);
    }

    private static double PivotLab(double t)
    {
        const double delta = 6.0 / 29.0;
        double delta3 = delta * delta * delta;
        if (t > delta3)
        {
            return Math.Cbrt(t);
        }

        return (t / (3.0 * delta * delta)) + (4.0 / 29.0);
    }

    private static double InversePivotLab(double t)
    {
        const double delta = 6.0 / 29.0;
        if (t > delta)
        {
            return t * t * t;
        }

        return 3.0 * delta * delta * (t - (4.0 / 29.0));
    }

    private readonly record struct LabColor(double L, double A, double B);
}
