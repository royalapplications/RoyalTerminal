// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace RoyalTerminal.Terminal;

/// <summary>Pure straight-alpha RGBA operations matching Ghostty's graphics_pixel and Wuffs swizzler.</summary>
internal static class ManagedKittyAnimationPixels
{
    internal static void ExpandRgb(ReadOnlySpan<byte> rgb, Span<byte> rgba, bool useSimd = true)
    {
        if (rgb.Length % 3 != 0 || (long)(rgb.Length / 3) * 4 > rgba.Length)
            throw new ArgumentException("RGB input and RGBA output lengths do not match.");
        if (rgb.Overlaps(rgba)) throw new ArgumentException("RGB expansion requires separate source and destination storage.");
        int source = 0;
        int destination = 0;
        if (useSimd && Vector128.IsHardwareAccelerated)
        {
            // Four packed RGB pixels become four RGBA pixels. A 16-byte read
            // needs four extra readable source bytes; leave that bounded tail
            // to the scalar loop instead of reading past a 12-byte group.
            Vector128<byte> shuffle = Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255);
            Vector128<byte> alpha = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);
            ref byte input = ref MemoryMarshal.GetReference(rgb);
            ref byte output = ref MemoryMarshal.GetReference(rgba);
            for (; source <= rgb.Length - Vector128<byte>.Count; source += 12, destination += 16)
            {
                Vector128<byte> packed = Vector128.LoadUnsafe(ref input, (nuint)source);
                (Vector128.Shuffle(packed, shuffle) | alpha).StoreUnsafe(ref output, (nuint)destination);
            }
        }
        for (; source < rgb.Length; source += 3, destination += 4)
        {
            rgba[destination] = rgb[source];
            rgba[destination + 1] = rgb[source + 1];
            rgba[destination + 2] = rgb[source + 2];
            rgba[destination + 3] = 255;
        }
    }

    internal static void Fill(Span<byte> rgba, uint background)
    {
        if ((rgba.Length & 3) != 0) throw new ArgumentException("RGBA storage must contain complete pixels.", nameof(rgba));
        if (background == 0)
        {
            rgba.Clear();
            return;
        }

        uint nativeColor = BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(background) : background;
        MemoryMarshal.Cast<byte, uint>(rgba).Fill(nativeColor);
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
                if (pixels[offset + 3] == 255 || target[offset + 3] == 0)
                {
                    pixels.Slice(offset, 4).CopyTo(target.Slice(offset, 4));
                    continue;
                }

                // Match Wuffs' 16-bit integer arithmetic, including intermediate
                // truncation. A floating-point blend differs for translucent pixels.
                // Do not skip a zero-alpha source: unpremultiplication rounding
                // can still change RGB bytes of a low-alpha destination.
                uint destinationAlpha = (uint)target[offset + 3] * 257;
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
