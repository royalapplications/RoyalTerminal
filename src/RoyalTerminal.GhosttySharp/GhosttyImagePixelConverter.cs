// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>Copies borrowed image pixels directly into the renderer's owned RGBA format.</summary>
internal static class GhosttyImagePixelConverter
{
    internal static byte[] CopyRgba(
        ReadOnlySpan<byte> source,
        GhosttyVtNative.GhosttyKittyImageFormat format,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        int channels = format switch
        {
            GhosttyVtNative.GhosttyKittyImageFormat.Rgba => 4,
            GhosttyVtNative.GhosttyKittyImageFormat.Rgb => 3,
            GhosttyVtNative.GhosttyKittyImageFormat.GrayAlpha => 2,
            GhosttyVtNative.GhosttyKittyImageFormat.Gray => 1,
            _ => 0,
        };
        int pixels = checked(width * height);
        if (channels == 0 || pixels == 0 || source.Length != checked(pixels * channels))
        {
            return [];
        }

        if (channels == 4)
        {
            return source.ToArray();
        }

        byte[] result = new byte[checked(pixels * 4)];
        for (int sourceIndex = 0, destinationIndex = 0; sourceIndex < source.Length; sourceIndex += channels, destinationIndex += 4)
        {
            byte first = source[sourceIndex];
            result[destinationIndex] = first;
            result[destinationIndex + 1] = channels == 3 ? source[sourceIndex + 1] : first;
            result[destinationIndex + 2] = channels == 3 ? source[sourceIndex + 2] : first;
            result[destinationIndex + 3] = channels == 2 ? source[sourceIndex + 1] : (byte)255;
        }

        return result;
    }
}
