// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty graphics_image.zig bounds images at 10,000 pixels per axis / 400 MiB.
/// xterm.js delegates PNG decoding to createImageBitmap; WT has no Kitty PNG decoder.
/// RoyalTerminal delegates to Skia and validates dimensions before native allocations.
/// </summary>
public sealed class SkiaKittyGraphicsPngDecoderTests
{
    [Fact]
    public void DecodesOwnedStraightAlphaRgba()
    {
        byte[] encoded = CreatePng();
        IKittyGraphicsPngDecoder decoder = new SkiaKittyGraphicsPngDecoder();

        Assert.True(decoder.TryDecode(encoded, 8, out KittyGraphicsDecodedImage? image));

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 128, 0, 255, 0, 255 }, image.Rgba);
        encoded.AsSpan().Clear();
        Assert.Equal(255, image.Rgba[0]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(7)]
    public void RejectsDecodedBudgetBeforeAllocation(int limit)
    {
        byte[] encoded = CreatePng();
        SkiaKittyGraphicsPngDecoder decoder = new();
        Assert.False(decoder.TryDecode(encoded, limit, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();

        bool decoded = decoder.TryDecode(encoded, limit, out KittyGraphicsDecodedImage? image);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(decoded);
        Assert.Null(image);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RejectsEncodedBudgetBeforeCopying()
    {
        byte[] encoded = CreatePng();
        SkiaKittyGraphicsPngDecoder decoder = new(encoded.Length - 1);

        Assert.False(decoder.TryDecode(encoded, 8, out KittyGraphicsDecodedImage? image));
        Assert.Null(image);
        Assert.True(new SkiaKittyGraphicsPngDecoder(encoded.Length).TryDecode(encoded, 8, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkiaKittyGraphicsPngDecoder(-1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(10001, 1)]
    [InlineData(1, 10001)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void RejectsOversizedHeaderBeforeCodecOrPixelAllocation(uint width, uint height)
    {
        byte[] encoded = CreatePng();
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(20), height);
        SkiaKittyGraphicsPngDecoder decoder = new();
        Assert.False(decoder.TryDecode(encoded, int.MaxValue, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();

        bool decoded = decoder.TryDecode(encoded, int.MaxValue, out KittyGraphicsDecodedImage? image);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(decoded);
        Assert.Null(image);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(33)]
    public void RejectsTruncatedPng(int length)
    {
        byte[] encoded = CreatePng();
        Assert.False(new SkiaKittyGraphicsPngDecoder().TryDecode(encoded.AsSpan(0, length), 8, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(25)]
    public void RejectsInvalidSignatureChunkOrColorType(int offset)
    {
        byte[] encoded = CreatePng();
        encoded[offset] = 255;
        Assert.False(new SkiaKittyGraphicsPngDecoder().TryDecode(encoded, 8, out _));
    }

    [Fact]
    public void RejectsOtherEncodedImageFormats()
    {
        using SKBitmap bitmap = new(1, 1);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        Assert.False(new SkiaKittyGraphicsPngDecoder().TryDecode(encoded.AsSpan(), 4, out _));
    }

    [Fact]
    public void DomainResultValidatesPixelLayoutAndTransfersOwnership()
    {
        byte[] pixels = [1, 2, 3, 4];
        Assert.Same(pixels, new KittyGraphicsDecodedImage(1, 1, pixels).Rgba);
        Assert.Throws<ArgumentNullException>(() => new KittyGraphicsDecodedImage(1, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KittyGraphicsDecodedImage(0, 1, pixels));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KittyGraphicsDecodedImage(1, 0, pixels));
        Assert.Throws<ArgumentException>(() => new KittyGraphicsDecodedImage(2, 1, pixels));
        Assert.Throws<ArgumentException>(() => new KittyGraphicsDecodedImage(int.MaxValue, int.MaxValue, pixels));
    }

    internal static byte[] CreatePng()
    {
        using SKBitmap bitmap = new(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 128));
        bitmap.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
