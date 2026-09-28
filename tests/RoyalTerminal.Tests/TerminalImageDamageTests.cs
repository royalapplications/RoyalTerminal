// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Reference decisions: Ghostty b40acce/src/renderer/generic.zig updates image
// state on scene changes (and every frame for virtual placements), then orders
// images around cell layers. WT src/renderer/atlas/BackendD2D.cpp tracks damaged
// row bounds including bitmap rendering; xterm.js ImageRenderer.clearLines clears
// row regions. Royal retains the composed Skia surface: ALL image layers must
// share row damage, or repainting an unchanged image corrupts clean text/alpha.
// Publishers may conservatively dirty the viewport. Tests that clear those flags
// isolate renderer damage detection; production never discards publisher damage.
public sealed class TerminalImageDamageTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void CleanComposedRowsAreNotOverpainted(bool raster, int layer)
    {
        TerminalScreen screen = new(4, 4);
        Publish(screen, raster, 0, 40, layer, 128);
        for (int row = 0; row < 4; row++)
        {
            Span<TerminalCell> cells = screen.GetViewportRow(row).Cells;
            for (int column = 0; column < cells.Length; column++)
                cells[column] = new() { Codepoint = 'X', Width = 1, Foreground = 0xffffffff, Background = 0xff008000 };
        }
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 40);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        byte[] before = bitmap.Bytes;
        screen.GetViewportRow(2).IsDirty = true;
        Retained(canvas, renderer, screen);
        byte[] after = bitmap.Bytes;
        Assert.Equal(before.AsSpan(0, bitmap.RowBytes * 20).ToArray(), after.AsSpan(0, bitmap.RowBytes * 20).ToArray());
        Assert.Equal(before.AsSpan(bitmap.RowBytes * 30).ToArray(), after.AsSpan(bitmap.RowBytes * 30).ToArray());
        // No row damage means no image draw or repeated alpha blending.
        renderer.ResetImageRenderDiagnostics();
        renderer.Render(canvas, screen);
        Assert.Equal(after, bitmap.Bytes);
        Assert.Equal(0, renderer.GetImageRenderDiagnostics().Draws);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovementAndDeletionDirtyOldAndNewBoundsOnly(bool raster)
    {
        TerminalScreen screen = new(4, 5);
        Publish(screen, raster, 1);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 50);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        Publish(screen, raster, 3);
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1, 3);
        // Repeated/abandoned preparation never advances the rendered baseline.
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1, 3);
        Retained(canvas, renderer, screen);
        Assert.NotEqual(SKColors.Red, bitmap.GetPixel(5, 15));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 35));
        ClearImages(screen, raster);
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 3);
        Retained(canvas, renderer, screen);
        Assert.NotEqual(SKColors.Red, bitmap.GetPixel(5, 35));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelReplacementSurvivesCowPublicationAndPreservesOtherDamage(bool raster)
    {
        TerminalScreen screen = new(4, 5);
        Publish(screen, raster, 1);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 50);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        TerminalScreen copy = screen.CreateStateCopy();
        Publish(copy, raster, 1, blue: true);
        screen.AdoptStateFrom(copy);
        Clean(screen);
        screen.GetViewportRow(4).IsDirty = true;
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1, 4);
        Retained(canvas, renderer, screen);
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(5, 15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FractionalBottomOverscanReceivesImageDamage(bool raster)
    {
        TerminalScreen screen = new(4, 3, 10);
        screen.AddRow();
        screen.ScrollOffset = 1;
        screen.RenderScrollFraction = 0.5;
        Publish(screen, raster, 3);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 30);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 27));
        Publish(screen, raster, 3, blue: true);
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 3);
        Retained(canvas, renderer, screen);
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(5, 27));
    }

    [Fact]
    public void DisjointDirtyRowsDoNotReblendTheCleanGap()
    {
        TerminalScreen screen = new(4, 5);
        Publish(screen, false, 0, 50, alpha: 128);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 50);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        SKColor middle = bitmap.GetPixel(5, 25);
        screen.GetViewportRow(0).IsDirty = true;
        screen.GetViewportRow(4).IsDirty = true;
        Retained(canvas, renderer, screen);
        Assert.Equal(middle, bitmap.GetPixel(5, 25));
    }

    [Fact]
    public void SourceAvailabilityAndPaintOrderArePartOfDamage()
    {
        TerminalScreen screen = new(4, 4);
        TerminalKittyImageSource red = new(1, 1, 1, [255, 0, 0, 255]);
        TerminalKittyImageSource blue = new(2, 1, 1, [0, 0, 255, 255]);
        TerminalKittyImagePlacement first = Kitty(1, 1, 10);
        TerminalKittyImagePlacement second = Kitty(2, 1, 10);
        screen.ReplaceKittyGraphics([red], [first, second]);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 40);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        screen.ReplaceKittyGraphics([red, blue], [first, second]);
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1);
        Retained(canvas, renderer, screen);
        // Give red a higher z-index instead of relying on sorting equal keys.
        screen.ReplaceKittyGraphics([red, blue], [Kitty(1, 1, 10, z: 1), second]);
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1);
        Retained(canvas, renderer, screen);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanVisibleBitmapsRemainProtectedOverBudget(bool raster)
    {
        TerminalScreen screen = new(4, 4);
        if (raster)
        {
            screen.ReplaceRasterImage(new(1, TerminalRasterImageProtocol.Sixel, 1, 1, [255, 0, 0, 255]), Raster(1, 0, 10));
            screen.ReplaceRasterImage(new(2, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 255, 255]), Raster(2, 2, 10));
        }
        else screen.ReplaceKittyGraphics([new(1, 1, 1, [255, 0, 0, 255]), new(2, 1, 1, [0, 0, 255, 255])],
            [Kitty(1, 0, 10), Kitty(2, 2, 10)]);
        using SkiaTerminalRenderer renderer = Renderer();
        renderer.ImageBitmapCacheBudgetBytes = 0;
        using SKBitmap bitmap = new(40, 40);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        renderer.ResetImageRenderDiagnostics();
        for (int row = 0; row <= 2; row += 2)
        {
            screen.GetViewportRow(row).IsDirty = true;
            Retained(canvas, renderer, screen);
        }
        Assert.Equal(0, renderer.GetImageRenderDiagnostics().CacheMisses);
        Assert.Equal(0, renderer.GetImageRenderDiagnostics().CacheEvictions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmUnchangedDamagePreparationAllocatesNothing(bool raster)
    {
        TerminalScreen screen = new(4, 4);
        Publish(screen, raster, 1);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 40);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        for (int index = 0; index < 100; index++) renderer.PrepareImageDamage(screen);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) renderer.PrepareImageDamage(screen);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        AssertDirty(screen);
    }

    [Fact]
    public void DamagePreparationRejectsNullScreen()
    {
        using SkiaTerminalRenderer renderer = Renderer();
        Assert.Throws<ArgumentNullException>(() => renderer.PrepareImageDamage(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CropAndLayerChangesDamageThePlacementEvenWhenPixelsAreUnchanged(bool changeLayer)
    {
        TerminalScreen screen = new(4, 4);
        TerminalKittyImageSource source = new(1, 2, 1, [255, 0, 0, 255, 0, 0, 255, 255]);
        screen.ReplaceKittyGraphics([source], [Kitty(1, 1, 10)]);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 40);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        TerminalKittyImageLayer layer = changeLayer ? TerminalKittyImageLayer.BelowBackground : TerminalKittyImageLayer.AboveText;
        screen.ReplaceKittyGraphics([source], [new(1, layer, 0, 1, 0, 0, 10, 10, changeLayer ? 0 : 1, 0, 1, 1)]);
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1);
        Retained(canvas, renderer, screen);
        if (!changeLayer) Assert.Equal(SKColors.Blue, bitmap.GetPixel(5, 15));
    }

    [Fact]
    public void ExplicitAboveOverscanTracksNegativeRowDamage()
    {
        TerminalScreen screen = new(4, 3, 10);
        screen.AddRow();
        screen.AddRow();
        screen.ScrollOffset = 1;
        TerminalRenderOverscan overscan = new(1, 1);
        Publish(screen, false, -1);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 50);
        using SKCanvas canvas = new(bitmap);
        canvas.Translate(0, 10);
        renderer.Render(canvas, screen, overscan, forceFullRedraw: true);
        Publish(screen, false, -1, blue: true);
        TerminalRenderViewport rows = screen.GetRenderViewport(overscan);
        for (int index = 0; index < rows.Count; index++) rows[index].Row.IsDirty = false;
        renderer.PrepareImageDamage(screen, overscan);
        for (int index = 0; index < rows.Count; index++) Assert.Equal(rows[index].ViewportY == -1, rows[index].Row.IsDirty);
    }

    [Fact]
    public void RejectedFrameDoesNotAcknowledgePendingDamageOrChangeCanvas()
    {
        TerminalScreen screen = new(4, 4);
        Publish(screen, false, 1);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKBitmap bitmap = new(40, 40);
        using SKCanvas canvas = new(bitmap);
        Full(canvas, renderer, screen);
        Publish(screen, false, 2);
        // Null is rejected before either canvas mutation or baseline commit.
        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!, screen));
        Clean(screen);
        renderer.PrepareImageDamage(screen);
        AssertDirty(screen, 1, 2);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 15));
    }

    private static void Publish(TerminalScreen screen, bool raster, int row, int height = 10, int layer = 2, byte alpha = 255, bool blue = false)
    {
        byte[] pixels = blue ? [0, 0, 255, alpha] : [255, 0, 0, alpha];
        if (raster) screen.ReplaceRasterImage(new(1, TerminalRasterImageProtocol.Sixel, 1, 1, pixels), Raster(1, row, height, layer));
        else screen.ReplaceKittyGraphics([new(1, 1, 1, pixels)], [Kitty(1, row, height, layer)]);
    }

    private static TerminalKittyImagePlacement Kitty(int id, int row, int height, int layer = 2, int z = 0)
        => new(id, (TerminalKittyImageLayer)layer, 0, row, 0, 0, 10, height, 0, 0, 1, 1, zIndex: z);
    private static TerminalRasterImagePlacement Raster(int id, int row, int height, int layer = 2)
        => new(id, (TerminalRasterImageLayer)layer, 0, row, 0, 0, 10, height, 0, 0, 1, 1, 10, 10);
    private static void ClearImages(TerminalScreen screen, bool raster)
    {
        if (raster) screen.ClearRasterGraphics();
        else screen.ClearKittyGraphics();
    }
    private static SkiaTerminalRenderer Renderer()
    {
        SkiaTerminalRenderer renderer = new("Consolas", 14) { CursorVisible = false, EnableImageRenderDiagnostics = true };
        renderer.SetCellSize(10, 10);
        return renderer;
    }
    private static void Clean(TerminalScreen screen)
    {
        TerminalRenderViewport rows = screen.GetRenderViewport(screen.RenderScrollOverscan);
        for (int index = 0; index < rows.Count; index++) rows[index].Row.IsDirty = false;
    }
    private static void AssertDirty(TerminalScreen screen, params int[] expected)
    {
        TerminalRenderViewport rows = screen.GetRenderViewport(screen.RenderScrollOverscan);
        for (int index = 0; index < rows.Count; index++) Assert.Equal(expected.Contains(rows[index].ViewportY), rows[index].Row.IsDirty);
    }
    private static void Full(SKCanvas canvas, SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        canvas.Clear(new SKColor(screen.DefaultBackground));
        renderer.RenderFull(canvas, screen);
    }
    private static void Retained(SKCanvas canvas, SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        renderer.PrepareImageDamage(screen);
        using SKPaint clear = new() { Color = new SKColor(screen.DefaultBackground), BlendMode = SKBlendMode.Src };
        canvas.Save();
        canvas.Translate(0, -(float)(screen.RenderScrollFraction * renderer.CellHeight));
        TerminalRenderViewport rows = screen.GetRenderViewport(screen.RenderScrollOverscan);
        for (int index = 0; index < rows.Count; index++)
            if (rows[index].Row.IsDirty) canvas.DrawRect(0, rows[index].ViewportY * renderer.CellHeight, screen.Columns * renderer.CellWidth, renderer.CellHeight, clear);
        canvas.Restore();
        renderer.Render(canvas, screen);
    }
}
