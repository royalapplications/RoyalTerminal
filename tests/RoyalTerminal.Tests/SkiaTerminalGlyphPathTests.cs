// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Glyphs;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SkiaTerminalGlyphPathTests
{
    [Fact]
    public void TriangleRendersDirectlyIntoDestinationWithForegroundAndYAxisTransform()
    {
        Assert.True(TerminalGlyphDecoder.TryDecode(Convert.FromBase64String("AAEAZABkA4QDhAACAAABAQEB9P5wAyADhPzgAAA="), out TerminalGlyphOutline? outline, out _));
        using SKPath path = SkiaTerminalGlyphPath.Create(outline);
        Assert.Equal(new SKRect(100, 100, 900, 900), path.Bounds);
        using SKBitmap bitmap = new(100, 100);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.Red, IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(0, 100);
        canvas.Scale(0.1f, -0.1f);
        canvas.DrawPath(path, paint);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 50));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 20));
        Assert.Equal(0, bitmap.GetPixel(15, 15).Alpha);
        Assert.Equal(0, bitmap.GetPixel(50, 95).Alpha);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AllOffCurveContourHasRotationInvariantImpliedMidpoints(int rotation)
    {
        TerminalGlyphPoint[] points = [new(50, 0, false), new(100, 50, false), new(50, 100, false), new(0, 50, false)];
        TerminalGlyphPoint[] rotated = new TerminalGlyphPoint[points.Length];
        for (int i = 0; i < points.Length; i++) rotated[i] = points[(i + rotation) % points.Length];
        using SKPath path = DecodePath([3], rotated);
        using SKBitmap bitmap = Render(path);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 50));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(20, 50));
        Assert.Equal(0, bitmap.GetPixel(5, 50).Alpha);
        Assert.Equal(0, bitmap.GetPixel(10, 10).Alpha);
        using SKPath referencePath = DecodePath([3], points);
        using SKBitmap reference = Render(referencePath);
        Assert.Equal(reference.Bytes, bitmap.Bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FirstAndLastOffCurvePointsUseTheSameClosedContour(int rotation)
    {
        TerminalGlyphPoint[] points = [new(10, 10, true), new(50, 90, false), new(90, 10, true)];
        TerminalGlyphPoint[] rotated = new TerminalGlyphPoint[points.Length];
        for (int i = 0; i < points.Length; i++) rotated[i] = points[(i + rotation) % points.Length];
        using SKPath path = DecodePath([2], rotated);
        using SKBitmap bitmap = Render(path);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 30));
        Assert.Equal(0, bitmap.GetPixel(50, 70).Alpha);
        using SKPath referencePath = DecodePath([2], points);
        using SKBitmap reference = Render(referencePath);
        Assert.Equal(reference.Bytes, bitmap.Bytes);
    }

    [Fact]
    public void OppositeContourWindingPreservesHolesAndRepeatedDrawingDoesNotAllocate()
    {
        using SKPath path = DecodePath([3, 7],
            [new(10, 10, true), new(90, 10, true), new(90, 90, true), new(10, 90, true),
             new(30, 30, true), new(30, 70, true), new(70, 70, true), new(70, 30, true)]);
        using SKBitmap bitmap = Render(path);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(20, 50));
        Assert.Equal(0, bitmap.GetPixel(50, 50).Alpha);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.Red };
        for (int i = 0; i < 10; i++) canvas.DrawPath(path, paint);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) canvas.DrawPath(path, paint);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void EmptyAndDegenerateGlyphsProduceNoCoverage()
    {
        foreach (TerminalGlyphPoint[] points in new TerminalGlyphPoint[][] { [], [new(50, 50, true)], [new(50, 50, false)] })
        {
            using SKPath path = DecodePath(points.Length == 0 ? [] : [0], points);
            using SKBitmap bitmap = Render(path);
            Assert.All(bitmap.Bytes, value => Assert.Equal(0, value));
        }
    }

    private static SKBitmap Render(SKPath path)
    {
        SKBitmap bitmap = new(100, 100);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.Red, IsAntialias = false };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPath(path, paint);
        return bitmap;
    }

    private static SKPath DecodePath(ushort[] ends, TerminalGlyphPoint[] points)
    {
        byte[] bytes = new byte[12 + ends.Length * 2 + points.Length * 5];
        BinaryPrimitives.WriteInt16BigEndian(bytes, checked((short)ends.Length));
        for (int i = 0; i < ends.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10 + i * 2), ends[i]);
        int offset = 12 + ends.Length * 2;
        for (int i = 0; i < points.Length; i++) bytes[offset++] = (byte)(points[i].OnCurve ? 1 : 0);
        int x = 0, y = 0;
        foreach (TerminalGlyphPoint point in points)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), checked((short)(point.X - x)));
            x = point.X;
            offset += 2;
        }
        foreach (TerminalGlyphPoint point in points)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), checked((short)(point.Y - y)));
            y = point.Y;
            offset += 2;
        }
        Assert.True(TerminalGlyphDecoder.TryDecode(bytes, out TerminalGlyphOutline? outline, out _));
        return SkiaTerminalGlyphPath.Create(outline);
    }
}
