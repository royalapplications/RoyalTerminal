// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Owned protocol pixels plus a lazy, immutable RGBA view for publication.
/// RGB storage remains RGB when rendered; animation promotion explicitly replaces
/// it with <see cref="AsRgba"/>. The owning processor serializes all access.
/// </summary>
internal sealed class ManagedKittyImagePixels
{
    private readonly byte[] _pixels;
    private readonly int _pendingBytes;
    private KittyGraphicsDecodedImage? _rgba;
    private TerminalKittyImageSource? _source;

    internal ManagedKittyImagePixels(KittyGraphicsDecodedImage rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        Width = rgba.Width;
        Height = rgba.Height;
        _pixels = rgba.Rgba;
        _rgba = rgba;
        Format = 1;
    }

    private ManagedKittyImagePixels(int width, int height, byte format, byte[] pixels, int pendingBytes = 0)
    {
        Width = width;
        Height = height;
        Format = format;
        _pixels = pixels;
        _pendingBytes = pendingBytes;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal byte Format { get; }
    internal bool IsRgba => Format == 1;
    internal bool IsPending => _pendingBytes > 0;
    internal int StorageBytes => IsPending ? _pendingBytes : _pixels.Length;
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
        return new(width, height, 0, rgb);
    }

    /// <summary>Reserves protocol storage before snapshot pixels arrive, without allocating a canvas.</summary>
    internal static ManagedKittyImagePixels Pending(int width, int height, byte format, int expectedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int channels = format switch { 0 => 3, 1 => 4, 3 => 2, 4 => 1, _ => 0 };
        ulong pixels = (ulong)width * (uint)height;
        if (channels == 0 || expectedBytes <= 0 || pixels * (uint)channels != (ulong)expectedBytes ||
            pixels * 4 > int.MaxValue)
            throw new ArgumentException("Pending pixels and their RGBA view must fit the specified format and dimensions.", nameof(expectedBytes));
        return new(width, height, format, [], expectedBytes);
    }

    internal ManagedKittyImagePixels? Complete(byte[] pixels) =>
        IsPending && pixels.Length == _pendingBytes ? new(Width, Height, Format, pixels) : null;

    internal KittyGraphicsDecodedImage GetRgbaImage(Action<ManagedKittyAnimationAllocation>? allocationCheckpoint = null)
    {
        if (IsPending) throw new InvalidOperationException("Image pixels have not arrived yet.");
        if (_rgba is not null) return _rgba;
        if (IsRgba)
        {
            allocationCheckpoint?.Invoke(ManagedKittyAnimationAllocation.RgbaView);
            return _rgba = new(Width, Height, _pixels);
        }
        allocationCheckpoint?.Invoke(ManagedKittyAnimationAllocation.RgbaBuffer);
        byte[] rgba = GC.AllocateUninitializedArray<byte>(RgbaByteLength);
        if (Format == 0) ManagedKittyAnimationPixels.ExpandRgb(_pixels, rgba);
        else
        {
            int channels = Format == 3 ? 2 : 1;
            for (int input = 0, output = 0; input < _pixels.Length; input += channels, output += 4)
            {
                rgba[output] = rgba[output + 1] = rgba[output + 2] = _pixels[input];
                rgba[output + 3] = channels == 2 ? _pixels[input + 1] : (byte)255;
            }
        }
        allocationCheckpoint?.Invoke(ManagedKittyAnimationAllocation.RgbaView);
        return _rgba = new(Width, Height, rgba);
    }

    /// <summary>
    /// Gets an immutable render source for these pixels. A frame's fingerprint is
    /// reused when animation playback returns to the same image ID. Keep only
    /// one ID-specific wrapper; sharing pixels under another image ID must not
    /// return the wrong identity or build an unbounded per-ID cache.
    /// </summary>
    internal TerminalKittyImageSource GetSource(int imageId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(imageId, 0);
        TerminalKittyImageSource? source = _source;
        if (source is null || source.ImageId != imageId)
        {
            KittyGraphicsDecodedImage rgba = GetRgbaImage();
            source = new(imageId, rgba.Width, rgba.Height, rgba.Rgba);
            _source = source;
        }
        return source;
    }

    internal ManagedKittyImagePixels AsRgba(Action<ManagedKittyAnimationAllocation>? allocationCheckpoint = null)
    {
        if (IsRgba) return this;
        KittyGraphicsDecodedImage rgba = GetRgbaImage(allocationCheckpoint);
        allocationCheckpoint?.Invoke(ManagedKittyAnimationAllocation.RgbaOwner);
        return new(rgba) { _source = _source };
    }
}
