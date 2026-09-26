// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Owned protocol pixels plus a lazy, immutable RGBA view for publication.
/// RGB storage remains RGB when rendered; animation promotion explicitly replaces
/// it with <see cref="AsRgba"/>. The owning processor serializes all access.
/// </summary>
internal sealed class ManagedKittyImagePixels
{
    private readonly byte[] _pixels;
    private KittyGraphicsDecodedImage? _rgba;

    internal ManagedKittyImagePixels(KittyGraphicsDecodedImage rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        Width = rgba.Width;
        Height = rgba.Height;
        _pixels = rgba.Rgba;
        _rgba = rgba;
        IsRgba = true;
    }

    private ManagedKittyImagePixels(int width, int height, byte[] rgb)
    {
        Width = width;
        Height = height;
        _pixels = rgb;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal bool IsRgba { get; }
    internal int StorageBytes => _pixels.Length;
    internal int RgbaByteLength => checked(Width * Height * 4);
    internal ReadOnlyMemory<byte> Data => _pixels;

    internal static ManagedKittyImagePixels FromRgb(int width, int height, byte[] rgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(rgb);
        ulong pixels = (ulong)width * (uint)height;
        if (pixels * 3 != (ulong)rgb.LongLength || pixels * 4 > int.MaxValue)
            throw new ArgumentException("RGB pixels and their RGBA view must fit the specified dimensions.", nameof(rgb));
        return new(width, height, rgb);
    }

    internal KittyGraphicsDecodedImage GetRgbaImage()
    {
        if (_rgba is not null) return _rgba;
        byte[] rgba = GC.AllocateUninitializedArray<byte>(RgbaByteLength);
        for (int source = 0, destination = 0; source < _pixels.Length; source += 3, destination += 4)
        {
            rgba[destination] = _pixels[source];
            rgba[destination + 1] = _pixels[source + 1];
            rgba[destination + 2] = _pixels[source + 2];
            rgba[destination + 3] = 255;
        }
        return _rgba = new(Width, Height, rgba);
    }

    internal ManagedKittyImagePixels AsRgba() => IsRgba ? this : new(GetRgbaImage());
}
