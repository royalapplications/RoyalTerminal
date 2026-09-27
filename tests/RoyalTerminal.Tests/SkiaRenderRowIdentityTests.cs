// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SkiaRenderRowIdentityTests
{
    [Fact]
    public void OverscanRowsRenderAtSignedPositionsWithoutMovingTheViewport()
    {
        TerminalScreen screen = new(5, 2, 100);
        screen.AddRow();
        screen.AddRow();
        SKColor[] colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow];
        for (int row = 0; row < screen.TotalRows; row++)
        {
            foreach (ref TerminalCell cell in screen.GetRow(row).Cells)
            {
                cell.Background = (uint)colors[row];
                cell.HasBackground = true;
            }
        }
        screen.ScrollOffset = 1;
        using SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(16, 20);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(80, 80));
        surface.Canvas.Translate(0, 20);
        renderer.Render(surface.Canvas, screen, new TerminalRenderOverscan(1, 1), forceFullRedraw: true);
        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        for (int row = 0; row < 4; row++)
        {
            Assert.Equal(colors[row], pixels.GetPixelColor(1, row * 20 + 1));
            Assert.False(screen.GetRow(row).IsDirty);
        }
        Assert.Equal(1, screen.ScrollOffset);
    }

    [Fact]
    public void HighlightCacheReusesUnchangedCowStorageAndSeparatesDetachedRows()
    {
        TerminalScreen screen = CreateScreen(3);
        using SkiaTerminalRenderer renderer = CreateRenderer();
        using SKSurface surface = SKSurface.Create(new SKImageInfo(32, 60));
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(3, renderer.TextHighlightRowCacheEntryCount);
        TerminalScreen copy = screen.CreateStateCopy();
        renderer.RenderFull(surface.Canvas, copy);
        Assert.Equal(3, renderer.TextHighlightRowCacheEntryCount);
        copy.GetRow(1).Cells[0].Codepoint = 'N';
        renderer.RenderFull(surface.Canvas, copy);
        Assert.Equal(4, renderer.TextHighlightRowCacheEntryCount);
        Assert.Equal(SKColors.Black, Pixel(surface, 21));
        Assert.Equal('O', screen.GetRow(1).ReadOnlyCells[0].Codepoint);
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(4, renderer.TextHighlightRowCacheEntryCount);
        Assert.Equal(SKColors.Red, Pixel(surface, 21));
    }

    [Fact]
    public void IdentityIsNotUsedAsAContentOrRuleRevision()
    {
        TerminalScreen screen = CreateScreen(1);
        using SkiaTerminalRenderer renderer = CreateRenderer();
        using SKSurface surface = SKSurface.Create(new SKImageInfo(32, 20));
        TerminalRenderRowId id = screen.GetRow(0).RenderId;
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(SKColors.Red, Pixel(surface, 1));
        screen.GetRow(0).Cells[0].Codepoint = 'N';
        Assert.Equal(id, screen.GetRow(0).RenderId);
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(1, renderer.TextHighlightRowCacheEntryCount);
        Assert.Equal(SKColors.Black, Pixel(surface, 1));
        renderer.SetTextHighlightRules([new() { Pattern = "NK", Background = (uint)SKColors.Blue }]);
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(id, screen.GetRow(0).RenderId);
        Assert.Equal(SKColors.Blue, Pixel(surface, 1));
    }

    [Fact]
    public void OverscanPreeditIsRestrictedToActualViewportRows()
    {
        TerminalScreen screen = CreateScreen(2);
        screen.AddRow();
        screen.AddRow();
        screen.ScrollOffset = 1;
        using SkiaTerminalRenderer renderer = CreateRenderer();
        renderer.CursorVisible = true;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(32, 80));
        surface.Canvas.Translate(0, 20);
        foreach (int cursorRow in new[] { -1, 2 })
        {
            renderer.CursorRow = cursorRow;
            renderer.CursorColumn = 0;
            renderer.Preedit = null;
            renderer.Render(surface.Canvas, screen, new TerminalRenderOverscan(1, 1), true);
            using SKImage baseline = surface.Snapshot();
            using SKPixmap expected = baseline.PeekPixels();
            renderer.Preedit = new TerminalPreedit("ZZ");
            renderer.Render(surface.Canvas, screen, new TerminalRenderOverscan(1, 1), true);
            using SKImage image = surface.Snapshot();
            using SKPixmap actual = image.PeekPixels();
            for (int y = 0; y < actual.Height; y++)
                for (int x = 0; x < actual.Width; x++)
                    Assert.Equal(expected.GetPixelColor(x, y), actual.GetPixelColor(x, y));
        }
    }

    [Fact]
    public void DefaultRenderDoesNotAcknowledgeOffViewportRows()
    {
        TerminalScreen screen = CreateScreen(2);
        screen.AddRow();
        using SkiaTerminalRenderer renderer = CreateRenderer();
        using SKSurface surface = SKSurface.Create(new SKImageInfo(32, 40));
        renderer.RenderFull(surface.Canvas, screen);
        Assert.True(screen.GetRow(0).IsDirty);
        Assert.False(screen.GetRow(1).IsDirty);
        Assert.False(screen.GetRow(2).IsDirty);
    }

    private static TerminalScreen CreateScreen(int rows)
    {
        TerminalScreen screen = new(2, rows, 100) { DefaultBackground = (uint)SKColors.Black };
        for (int row = 0; row < rows; row++)
        {
            foreach (ref TerminalCell cell in screen.GetRow(row).Cells)
            {
                cell.Background = (uint)SKColors.Black;
                cell.HasBackground = true;
            }
            screen.GetRow(row).Cells[0].Codepoint = 'O';
            screen.GetRow(row).Cells[1].Codepoint = 'K';
        }
        return screen;
    }

    private static SkiaTerminalRenderer CreateRenderer()
    {
        SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false, TextHighlightingMode = TerminalTextHighlightingMode.Static };
        renderer.SetCellSize(16, 20);
        renderer.SetTextHighlightRules([new() { Pattern = "OK", Background = (uint)SKColors.Red }]);
        return renderer;
    }

    private static SKColor Pixel(SKSurface surface, int y)
    {
        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        return pixels.GetPixelColor(1, y);
    }
}
