// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalCursorAppearanceTests
{
    [Theory]
    [InlineData(false, true, true, true, true, true, null)]
    [InlineData(true, true, false, false, true, false, CursorStyle.Lock)]
    [InlineData(true, true, true, true, true, false, CursorStyle.Lock)]
    [InlineData(true, false, false, false, false, true, null)]
    [InlineData(true, false, true, false, true, false, CursorStyle.BlockHollow)]
    [InlineData(true, false, true, true, true, false, null)]
    [InlineData(true, false, true, true, true, true, CursorStyle.Bar)]
    [InlineData(true, false, true, true, false, false, CursorStyle.Bar)]
    public void AppearanceFollowsGhosttyPriority(bool inViewport, bool password, bool visible,
        bool focused, bool blinking, bool blinkVisible, CursorStyle? expected)
    {
        Assert.Equal(expected, TerminalCursorAppearance.Resolve(CursorStyle.Bar, inViewport,
            password, visible, focused, blinking, blinkVisible));
    }

    [Theory]
    [InlineData(8, 16)]
    [InlineData(16, 32)]
    [InlineData(32, 32)]
    public void LockFallbackHasShackleAndBodyAndStaysInsideCell(int width, int height)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width + 4, height + 4));
        surface.Canvas.Clear(SKColors.Black);
        using SKPaint paint = new() { Color = SKColors.Red };
        SkiaTerminalRenderer.DrawPasswordLockFallback(surface.Canvas, new SKRect(2, 2, width + 2, height + 2), paint);
        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        Assert.Equal(SKColors.Red, pixels.GetPixelColor(2 + width / 2, 2 + height * 3 / 4));
        Assert.Equal(SKColors.Black, pixels.GetPixelColor(2 + width / 2, 2 + height / 3));
        bool shackle = false;
        for (int y = 0; y < height + 4; y++)
        for (int x = 0; x < width + 4; x++)
        {
            SKColor pixel = pixels.GetPixelColor(x, y);
            if (x < 2 || y < 2 || x >= width + 2 || y >= height + 2) Assert.Equal(SKColors.Black, pixel);
            if (y >= 2 && y < 2 + height / 3 && pixel == SKColors.Red) shackle = true;
        }
        Assert.True(shackle);
        Assert.Equal(SKPaintStyle.Fill, paint.Style);
        Assert.Equal(0, paint.StrokeWidth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockCursorRendersAndWideTailMatchesLead(bool wide)
    {
        using SkiaTerminalRenderer renderer = new() { CursorStyle = CursorStyle.Lock, CursorColor = SKColors.Red };
        TerminalScreen screen = new(4, 1);
        if (wide)
        {
            screen.GetViewportRow(0)[0].Width = 2;
            screen.GetViewportRow(0)[1].Width = 0;
        }
        int width = (int)Math.Ceiling(renderer.CellWidth * 4);
        int height = (int)Math.Ceiling(renderer.CellHeight);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        renderer.RenderFull(surface.Canvas, screen);
        using SKImage lead = surface.Snapshot();
        using SKPixmap leadPixels = lead.PeekPixels();
        bool hasLock = false;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            SKColor pixel = leadPixels.GetPixelColor(x, y);
            if (pixel.Red > 200 && pixel.Green < 50 && pixel.Blue < 50) hasLock = true;
        }
        Assert.True(hasLock);
        renderer.CursorColumn = wide ? 1 : 0;
        renderer.RenderFull(surface.Canvas, screen);
        using SKImage tail = surface.Snapshot();
        using SKPixmap tailPixels = tail.PeekPixels();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) Assert.Equal(leadPixels.GetPixelColor(x, y), tailPixels.GetPixelColor(x, y));
    }
}
