// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyPngDecoderTests
{
    [GhosttyNativeFact]
    public void NativeHookDecodesStraightAlphaRgbaWithSharedBoundedCodec()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        byte[] png = CreatePng();
        GhosttySys.EnsureSkiaPngDecoderInstalled();
        using GhosttyTerminal terminal = new(80, 24);
        terminal.SetKittyImageStorageLimit(1024);

        terminal.Write(Encoding.ASCII.GetBytes($"\u001b_Ga=t,t=d,f=100,i=1;{Convert.ToBase64String(png)}\u001b\\"));

        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage image));
        Assert.Equal(1u, image.GetWidth());
        Assert.Equal(1u, image.GetHeight());
        Assert.Equal(new byte[] { 255, 0, 0, 128 }, image.CopyRgbaData());
    }

    [GhosttyNativeFact]
    public void NativeHookRejectsOversizedHeaderWithoutLosingSubsequentInput()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        byte[] png = CreatePng();
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 10_001);
        GhosttySys.EnsureSkiaPngDecoderInstalled();
        using GhosttyTerminal terminal = new(80, 24);
        terminal.SetKittyImageStorageLimit(1024);

        terminal.Write(Encoding.ASCII.GetBytes($"\u001b_Ga=t,t=d,f=100,i=1;{Convert.ToBase64String(png)}\u001b\\"));
        terminal.Write("\u001b_Ga=t,t=d,f=24,i=2,s=1,v=1;/wAA\u001b\\"u8);

        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.False(graphics!.TryGetImage(1, out _));
        Assert.True(graphics.TryGetImage(2, out GhosttyKittyGraphicsImage image));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.CopyRgbaData());
    }

    private static byte[] CreatePng()
    {
        using SKBitmap bitmap = new(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 128));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
