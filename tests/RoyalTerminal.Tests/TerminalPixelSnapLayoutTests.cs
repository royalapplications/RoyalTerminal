// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty GTK scale.zig (#14269) is the edge-snapping oracle. xterm.js uses
/// devicePixelContentBoxSize (DevicePixelObserver.ts) to avoid fractional-DPI
/// resampling; Windows Terminal AtlasEngine.SetWindowSize accepts physical
/// pixels. Avalonia instead supplies a logical rect plus a Skia device matrix.
/// We snap the shared retained image, not logical input/IME or grid coordinates.
/// Direct native GPU target descriptors are already physical and are untouched.
/// </summary>
public sealed class TerminalPixelSnapLayoutTests
{
    [Theory]
    [InlineData(101, 1.25f, 0, 126, 0)]
    [InlineData(101, 1.25f, 0.4f, 127, -0.32f)]
    [InlineData(101, 1.25f, 0.6f, 126, 0.32f)]
    [InlineData(101, 1.25f, -0.4f, 126, 0.32f)]
    [InlineData(101, 1.25f, -0.6f, 127, -0.32f)]
    [InlineData(100, 1.5f, 0.5f, 150, 1f / 3)]
    [InlineData(100, 1.5f, -0.5f, 151, -1f / 3)]
    [InlineData(800, 2, 0, 1600, 0)]
    [InlineData(0, 1.25f, 0.4f, 0, -0.32f)]
    [InlineData(1, 0.25f, 0, 0, 0)]
    public void SnapsBothDeviceEdges(float logical, float scale, float origin, int pixels, float offset)
    {
        SKMatrix matrix = Transform(scale, scale, origin, origin);
        Assert.True(TerminalPixelSnapLayout.TryCreate(logical, logical, scale, scale, matrix, out var layout));
        Assert.Equal(pixels, layout.Width);
        Assert.Equal(pixels, layout.Height);
        Assert.Equal(offset, layout.Destination.Left, precision: 5);
        Assert.Equal(offset, layout.Destination.Top, precision: 5);
        Assert.Equal(pixels / scale, layout.Destination.Width, precision: 4);
        Assert.Equal(pixels / scale, layout.Destination.Height, precision: 4);
    }

    [Fact]
    public void UsesIndependentAxesAndFullVisualBoundsNotDamageClip()
    {
        SKMatrix matrix = Transform(1.25f, 1.5f, 0.4f, 0.6f);
        Rect bounds = new(0, 0, 101, 51);
        SKRect clip = new(0, 0, 20, 10);
        var scale = TerminalDrawHandler.GetCanvasScale(bounds, clip, matrix);
        var logical = TerminalDrawHandler.GetRenderTargetLogicalSize(bounds, clip);
        Assert.True(TerminalPixelSnapLayout.TryCreate(logical.Width, logical.Height,
            scale.X, scale.Y, matrix, out var layout));
        Assert.Equal(127, layout.Width);
        Assert.Equal(76, layout.Height);
        Assert.Equal(-0.32f, layout.Destination.Left, precision: 5);
        Assert.Equal(0.4f / 1.5f, layout.Destination.Top, precision: 5);
    }

    [Theory]
    [InlineData(0)] // rotation
    [InlineData(1)] // skew
    [InlineData(2)] // reflection
    [InlineData(3)] // perspective X
    [InlineData(4)] // perspective Y
    [InlineData(5)] // non-normalized perspective
    [InlineData(6)] // clip-inferred DPI
    public void UnsupportedMappingRetainsExistingFallback(int kind)
    {
        SKMatrix matrix = Transform(1.25f, 1.25f, 0.4f, 0.4f);
        switch (kind)
        {
            case 0: matrix = SKMatrix.CreateRotationDegrees(30); break;
            case 1: matrix.SkewX = 0.1f; break;
            case 2: matrix.ScaleX = -1.25f; break;
            case 3: matrix.Persp0 = 0.001f; break;
            case 4: matrix.Persp1 = 0.001f; break;
            case 5: matrix.Persp2 = 2; break;
            case 6: matrix = SKMatrix.Identity; break;
        }

        Assert.False(TerminalPixelSnapLayout.TryCreate(101, 101, 1.25f, 1.25f, matrix, out var layout));
        Assert.Equal(default, layout);
        Assert.Equal((127, 127), TerminalDrawHandler.GetRenderTargetPixelSize(
            new Rect(0, 0, 101, 101), new SKRect(0, 0, 20, 20), 1.25f, 1.25f));
    }

    [Theory]
    [InlineData(float.NaN, 100, 1, 0)]
    [InlineData(float.PositiveInfinity, 100, 1, 0)]
    [InlineData(-1, 100, 1, 0)]
    [InlineData(100, float.NaN, 1, 0)]
    [InlineData(100, 100, 0, 0)]
    [InlineData(100, 100, -1, 0)]
    [InlineData(100, 100, float.NaN, 0)]
    [InlineData(100, 100, float.PositiveInfinity, 0)]
    [InlineData(100, 100, 1, float.NaN)]
    [InlineData(100, 100, 1, float.PositiveInfinity)]
    [InlineData(float.MaxValue, 100, 2, 0)]
    public void RejectsInvalidOrUnrepresentableGeometry(float width, float height, float scale, float origin)
    {
        Assert.False(TerminalPixelSnapLayout.TryCreate(width, height, scale, scale,
            Transform(scale, scale, origin, origin), out var layout));
        Assert.Equal(default, layout);
    }

    [Theory]
    [InlineData(1.25f, 10.4f, 11.6f)]
    [InlineData(1.5f, 10.6f, 11.4f)]
    [InlineData(2, 10.25f, 11.75f)]
    public void CompositedPixelsMatchSourceWithoutInterpolation(float scale, float x, float y)
    {
        SKMatrix matrix = Transform(scale, scale, x, y);
        Assert.True(TerminalPixelSnapLayout.TryCreate(13, 9, scale, scale, matrix, out var layout));
        using SKBitmap source = new(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int row = 0; row < source.Height; row++)
        {
            for (int column = 0; column < source.Width; column++)
            {
                source.SetPixel(column, row, ((row + column) & 1) == 0 ? SKColors.Red : SKColors.Blue);
            }
        }

        using SKImage image = SKImage.FromBitmap(source);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(64, 64));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.SetMatrix(matrix);
        surface.Canvas.ClipRect(new SKRect(0, 0, 13, 9), SKClipOperation.Intersect, antialias: false);
        // Linear filtering deliberately exposes subpixel translation/stretching.
        surface.Canvas.DrawImage(image, layout.Destination, new SKSamplingOptions(SKFilterMode.Linear));
        using SKImage result = surface.Snapshot();
        using SKBitmap actual = SKBitmap.FromImage(result);
        int left = (int)Math.Round(x, MidpointRounding.AwayFromZero);
        int top = (int)Math.Round(y, MidpointRounding.AwayFromZero);
        for (int row = 0; row < source.Height; row++)
        {
            for (int column = 0; column < source.Width; column++)
            {
                Assert.Equal(source.GetPixel(column, row), actual.GetPixel(left + column, top + row));
            }
        }
        // Premultiplied storage canonicalizes alpha-zero pixels to transparent
        // black; SKColors.Transparent is transparent white before premultiply.
        SKColor transparentPixel = new(0, 0, 0, 0);
        Assert.Equal(transparentPixel, actual.GetPixel(left - 1, top));
        Assert.Equal(transparentPixel, actual.GetPixel(left + source.Width, top));
    }

    [Fact]
    public void OriginOnlyMovementCanReuseSameSizedRaster()
    {
        Assert.True(TerminalPixelSnapLayout.TryCreate(100, 100, 1.25f, 1.25f,
            Transform(1.25f, 1.25f, 0.1f, 0.1f), out var first));
        Assert.True(TerminalPixelSnapLayout.TryCreate(100, 100, 1.25f, 1.25f,
            Transform(1.25f, 1.25f, 0.4f, 0.4f), out var moved));
        Assert.Equal(first.Width, moved.Width);
        Assert.Equal(first.Height, moved.Height);
        Assert.NotEqual(first.Destination, moved.Destination);
    }

    [Fact]
    public void LayoutComputationDoesNotAllocate()
    {
        SKMatrix matrix = Transform(1.25f, 1.25f, 0.4f, 0.6f);
        TerminalPixelSnapLayout.TryCreate(101, 101, 1.25f, 1.25f, matrix, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int i = 0; i < 10_000; i++)
        {
            if (TerminalPixelSnapLayout.TryCreate(101, 101, 1.25f, 1.25f, matrix, out var layout))
            {
                total += layout.Width;
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1_270_000, total);
        Assert.Equal(0, allocated);
    }

    private static SKMatrix Transform(float scaleX, float scaleY, float x, float y)
    {
        SKMatrix matrix = SKMatrix.CreateScale(scaleX, scaleY);
        matrix.TransX = x;
        matrix.TransY = y;
        return matrix;
    }
}
