// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Sixel - Decoded image model.

namespace RoyalTerminal.Sixel;

/// <summary>
/// Immutable decoded sixel image payload.
/// </summary>
public sealed class SixelImage
{
    /// <summary>
    /// Creates a decoded sixel image.
    /// </summary>
    public SixelImage(int width, int height, byte[] rgbaPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(rgbaPixels);

        int expectedLength = checked(width * height * 4);
        if (rgbaPixels.Length != expectedLength)
        {
            throw new ArgumentException("RGBA payload length does not match image dimensions.", nameof(rgbaPixels));
        }

        Width = width;
        Height = height;
        RgbaPixels = rgbaPixels;
    }

    /// <summary>Decoded image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Decoded image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Decoded RGBA pixels, four bytes per pixel.</summary>
    public byte[] RgbaPixels { get; }
}
