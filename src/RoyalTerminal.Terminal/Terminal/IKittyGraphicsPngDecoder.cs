// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;

namespace RoyalTerminal.Terminal;

/// <summary>Decodes Kitty PNG payloads without coupling the VT parser to an imaging framework.</summary>
public interface IKittyGraphicsPngDecoder
{
    /// <summary>
    /// Decodes one complete PNG into tightly packed, straight-alpha RGBA pixels.
    /// Implementations must check dimensions and <paramref name="maxDecodedBytes"/> before
    /// allocating pixels, reject malformed or oversized input, and transfer pixel ownership
    /// to the caller on success. The encoded span is borrowed only for this call.
    /// </summary>
    bool TryDecode(
        ReadOnlySpan<byte> encoded,
        int maxDecodedBytes,
        [NotNullWhen(true)] out KittyGraphicsDecodedImage? image);
}

/// <summary>A decoded image whose tightly packed RGBA buffer belongs to the caller.</summary>
public sealed class KittyGraphicsDecodedImage
{
    /// <summary>Creates an image and transfers ownership of <paramref name="rgba"/> to its consumer.</summary>
    public KittyGraphicsDecodedImage(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(rgba);
        if ((ulong)width * (uint)height * 4 != (ulong)rgba.LongLength)
        {
            throw new ArgumentException("The RGBA buffer must contain exactly four bytes per pixel.", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the owned row-major red, green, blue, straight-alpha pixel buffer.</summary>
    public byte[] Rgba { get; }
}
