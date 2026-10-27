// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty 4a4fc0179 render.ImagePlacement exports an anonymous placement as
// placement_id=0 and omits requested c/r. Keep storage-backed scale metadata:
// equal integer geometry is insufficient when Skia has fractional cell sizes.
public sealed class NativeKittyRenderGenerationTests
{
    [Fact]
    public void TextOnlyRefreshesAvoidPlacementAllocationsWhileResizeScrollAndPixelsStillRefresh()
    {
        if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        TerminalScreen screen = new(20, 3, 10);
        using GhosttyVtProcessor processor = new(screen);
        processor.NotifyResize(20, 3, 160, 48);
        processor.Process("\x1b_Ga=t,f=32,i=1,s=1,v=1;/wAA/w==\x1b\\"u8);
        for (int i = 1; i <= 128; i++)
            processor.Process(Encoding.ASCII.GetBytes($"\x1b_Ga=p,i=1,p={i},c=1,r=1,C=1\x1b\\"));
        TerminalKittyImagePlacement original = screen.GetKittyPlacements()[0];
        for (int i = 0; i < 200; i++) TextFrame(processor, i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) TextFrame(processor, i);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes < 51_200, $"Text-only refresh allocated {bytes} bytes for unchanged images.");
        Assert.Same(original, screen.GetKittyPlacements()[0]);

        processor.NotifyResize(20, 3, 320, 96);
        Assert.Equal(16, screen.GetKittyPlacements()[0].WidthPx);
        Assert.Equal(16, screen.GetKittyPlacements()[0].CellWidthPx);
        processor.Process("\x1b_Ga=f,f=32,i=1,r=1,s=1,v=1;AAD//w==\x1b\\"u8);
        Assert.True(screen.TryGetKittyImageSource(1, out var image));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image!.RgbaPixels);
        processor.Process("\x1b[3;1H\r\n"u8);
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        processor.ScrollViewportToTop();
        Assert.Equal(128, screen.GetKittyPlacements().Length);
    }

    private static void TextFrame(GhosttyVtProcessor processor, int i)
        => processor.Process((i & 1) == 0 ? "\x1b[3;1HA"u8 : "\x1b[3;1HB"u8);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousIntrinsicAndGridSizedPlacementsRetainDistinctFractionalScaling(bool native)
    {
        if (native && (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)) return;
        TerminalScreen screen = new(10, 3, 0);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.NotifyResize(10, 3, 80, 48);
        byte[] pixels = new byte[8 * 16 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 255; pixels[i + 3] = 255; }
        processor.Process(Encoding.ASCII.GetBytes($"\x1b_Ga=t,f=32,i=1,s=8,v=16;{Convert.ToBase64String(pixels)}\x1b\\"));
        processor.Process("\x1b_Ga=p,i=1,C=1\x1b\\\x1b[1;3H\x1b_Ga=p,i=1,c=1,r=1,C=1\x1b\\"u8);
        TerminalKittyImagePlacement[] placements = screen.GetKittyPlacements().ToArray();
        Assert.Equal(2, placements.Length);
        TerminalKittyImagePlacement intrinsic = Assert.Single(placements, p => p.ViewportColumn == 0);
        TerminalKittyImagePlacement scaled = Assert.Single(placements, p => p.ViewportColumn == 2);
        Assert.Equal((8, 16), (intrinsic.WidthPx, intrinsic.HeightPx));
        Assert.Equal((8, 16), (scaled.WidthPx, scaled.HeightPx));
        Assert.Equal(TerminalKittyImagePlacementScaleMode.None, intrinsic.ScaleMode);
        Assert.Equal(TerminalKittyImagePlacementScaleMode.ColumnsAndRows, scaled.ScaleMode);
        Assert.Equal((0, 0), (intrinsic.CellWidthPx, intrinsic.CellHeightPx));
        Assert.Equal((8, 16), (scaled.CellWidthPx, scaled.CellHeightPx));

        using SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(10.5f, 20.5f);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(105, 62));
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);
        using SKImage snapshot = surface.Snapshot();
        using SKPixmap rendered = snapshot.PeekPixels();
        Assert.Equal(SKColors.Red, rendered.GetPixelColor(6, 8));
        Assert.NotEqual(SKColors.Red, rendered.GetPixelColor(9, 8));
        Assert.Equal(SKColors.Red, rendered.GetPixelColor(30, 8));
    }
}
