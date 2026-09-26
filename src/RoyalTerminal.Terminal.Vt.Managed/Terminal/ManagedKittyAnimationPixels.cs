// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Pure straight-alpha RGBA operations matching Ghostty's graphics_pixel and Wuffs swizzler.</summary>
internal static class ManagedKittyAnimationPixels
{
    internal static void Fill(Span<byte> rgba, uint background)
    {
        if (background == 0)
        {
            rgba.Clear();
            return;
        }

        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = (byte)(background >> 24);
            rgba[offset + 1] = (byte)(background >> 16);
            rgba[offset + 2] = (byte)(background >> 8);
            rgba[offset + 3] = (byte)background;
        }
    }

    internal static void Compose(Span<byte> destination, int destinationStride, ReadOnlySpan<byte> source,
        int sourceStride, int width, int height, int sourceX, int sourceY, int destinationX, int destinationY,
        bool overwrite)
    {
        for (int row = 0; row < height; row++)
        {
            Span<byte> target = destination.Slice(((destinationY + row) * destinationStride + destinationX) * 4, width * 4);
            ReadOnlySpan<byte> pixels = source.Slice(((sourceY + row) * sourceStride + sourceX) * 4, width * 4);
            if (overwrite)
            {
                pixels.CopyTo(target);
                continue;
            }

            for (int offset = 0; offset < target.Length; offset += 4)
            {
                uint destinationAlpha = (uint)target[offset + 3] * 257;
                if (destinationAlpha == 0)
                {
                    pixels.Slice(offset, 4).CopyTo(target.Slice(offset, 4));
                    continue;
                }

                // Match Wuffs' 16-bit integer arithmetic, including intermediate
                // truncation. A floating-point blend differs for translucent pixels.
                uint sourceAlpha = (uint)pixels[offset + 3] * 257;
                uint inverse = 65535 - sourceAlpha;
                uint alpha = sourceAlpha + destinationAlpha * inverse / 65535;
                for (int channel = 0; channel < 3; channel++)
                {
                    uint premultiplied = (uint)target[offset + channel] * 257 * destinationAlpha / 65535;
                    uint blended = ((uint)pixels[offset + channel] * 257 * sourceAlpha + premultiplied * inverse) / 65535;
                    target[offset + channel] = (byte)((blended * 65535 / alpha) >> 8);
                }
                target[offset + 3] = (byte)(alpha >> 8);
            }
        }
    }
}
