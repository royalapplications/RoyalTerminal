// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Sixel decoder tests.

using System.Text;
using RoyalTerminal.Sixel;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SixelDecoderTests
{
    [Fact]
    public void Decode_RgbColorAndSingleColumn_ReturnsRgbaImage()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("q#1;2;100;0;0#1@"));

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Image);
        Assert.Equal(1, result.Image!.Width);
        Assert.Equal(12, result.Image.Height);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], result.Image.RgbaPixels.AsSpan(0, 4).ToArray());
        Assert.Equal(1, result.FinalCursorX);
        Assert.Equal(0, result.FinalCursorY);
    }

    [Fact]
    public void Decode_RepeatCommand_DrawsRepeatedColumns()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("q#1;2;0;100;0#1!3@"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, result.Image!.Width);
        Assert.Equal(3, result.FinalCursorX);
        for (int pixel = 0; pixel < 3; pixel++)
        {
            int offset = pixel * 4;
            Assert.Equal(0x00, result.Image.RgbaPixels[offset]);
            Assert.Equal(0xFF, result.Image.RgbaPixels[offset + 1]);
            Assert.Equal(0x00, result.Image.RgbaPixels[offset + 2]);
            Assert.Equal(0xFF, result.Image.RgbaPixels[offset + 3]);
        }
    }

    [Fact]
    public void Decode_SequentialColumns_ReportsDrawnWidthNotGrowthCapacity()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("qAAA"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, result.Image!.Width);
        Assert.Equal(12, result.Image.Height);
        Assert.Equal(3 * 12 * 4, result.Image.RgbaPixels.Length);
        Assert.Equal(3, result.FinalCursorX);
        Assert.Equal(0xFF, GetAlpha(result.Image, x: 2, y: 2));
    }

    [Fact]
    public void Decode_HlsColorDefinition_ConvertsToRgb()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("q#1;1;240;50;100#1@"));

        Assert.True(result.Success, result.Message);
        Assert.True(result.Image!.RgbaPixels[1] > 200);
        Assert.Equal(0xFF, result.Image.RgbaPixels[3]);
    }

    [Fact]
    public void Decode_RasterAttributesAtStart_UpdateAspectRatioAndDeclaredSize()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("q\"1;1;2;6#1;2;100;0;0#1@"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Image!.Width);
        Assert.Equal(6, result.Image.Height);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], result.Image.RgbaPixels.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void Decode_RasterAttributes_PerformCarriageReturnWithoutShrinkingExistingContent()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("q#1;2;100;0;0#1A\"1;1;2;6@"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Image!.Width);
        Assert.Equal(12, result.Image.Height);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], result.Image.RgbaPixels.AsSpan(0, 4).ToArray());
        Assert.Equal(1, result.FinalCursorX);
    }

    [Fact]
    public void Decode_ColorRegisterNumbers_WrapIntoConfiguredTable()
    {
        SixelDecoder decoder = new(new SixelDecoderOptions
        {
            MaxColorRegisters = 16,
        });

        SixelDecodeResult result = decoder.Decode(Ascii("q#17;2;100;0;0#17@"));

        Assert.True(result.Success, result.Message);
        Assert.Equal([0xFF, 0x00, 0x00, 0xFF], result.Image!.RgbaPixels.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void Decode_TooWideImage_ReturnsImageTooLarge()
    {
        SixelDecoder decoder = new(new SixelDecoderOptions
        {
            MaxWidth = 2,
            MaxHeight = 16,
            MaxPixels = 32,
        });

        SixelDecodeResult result = decoder.Decode(Ascii("q!3@"));

        Assert.Equal(SixelDecodeStatus.ImageTooLarge, result.Status);
        Assert.False(result.Success);
    }

    [Fact]
    public void Decode_OverflowingNumericParameter_ReturnsInvalidData()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("q!" + new string('9', 32) + "@"));

        Assert.Equal(SixelDecodeStatus.InvalidData, result.Status);
        Assert.False(result.Success);
    }

    [Fact]
    public void Decode_DecrqssDcsPayload_IsNotSixel()
    {
        SixelDecoder decoder = new();

        SixelDecodeResult result = decoder.Decode(Ascii("$qm"));

        Assert.Equal(SixelDecodeStatus.MissingIntroducer, result.Status);
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static byte GetAlpha(SixelImage image, int x, int y)
        => image.RgbaPixels[(((y * image.Width) + x) * 4) + 3];
}
