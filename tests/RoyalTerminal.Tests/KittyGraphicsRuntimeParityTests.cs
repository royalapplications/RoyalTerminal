// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class KittyGraphicsRuntimeParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingImages_RenderInZThenUnsignedImageIdOrder(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 3, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 3, 160, 60);
        // Deliberately insert the highest unsigned ID first and the lowest z last.
        processor.Process("\u001b_Ga=T,f=32,i=4294967295,p=1,s=1,v=1,c=1,r=1,z=5,C=1;AP8A/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,c=1,r=1,z=5,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,c=1,r=1,z=1,C=1;AAD//w==\u001b\\"u8);
        ReadOnlySpan<TerminalKittyImagePlacement> placements = screen.GetKittyPlacements();
        Assert.Equal(3, placements.Length);
        Assert.Equal(1, placements[0].ImageId);
        Assert.Equal(1, placements[0].ZIndex);
        Assert.Equal(2, placements[1].ImageId);
        Assert.Equal(-1, placements[2].ImageId);

        using SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(20, 20);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(160, 60));
        surface.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(surface.Canvas, screen);
        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        Assert.Equal(SKColors.Lime, pixels.GetPixelColor(10, 10));

        // Change only z: the native equality fast path must still publish it.
        processor.Process("\u001b_Ga=p,i=1,p=1,c=1,r=1,z=9,C=1\u001b\\"u8);
        Assert.Equal(1, screen.GetKittyPlacements()[2].ImageId);
        Assert.Equal(9, screen.GetKittyPlacements()[2].ZIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetClearsBothImageStoresAndReturnsToPrimary(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 3, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b[?1049h\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,C=1;AAD//w==\u001b\\"u8);
        processor.Process("\u001bc"u8);
        Assert.False(processor.AlternateScreen);
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b_Ga=p,i=1,p=2,C=1\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b[?1049h\u001b_Ga=p,i=2,p=2,C=1\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b[?1049l\u001b_Ga=T,f=32,i=3,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
        Assert.Equal(3, Assert.Single(screen.GetKittyPlacements().ToArray()).ImageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoftResetPreservesGraphicsAndActiveBuffer(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 3, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process("\u001b[?1049h\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,C=1;AAD//w==\u001b\\"u8);
        processor.Process("\u001b[!p"u8);
        Assert.True(processor.AlternateScreen);
        Assert.Equal(2, Assert.Single(screen.GetKittyPlacements().ToArray()).ImageId);
        processor.Process("\u001b_Ga=d,d=i,i=2\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EraseDisplayDiscardsVisibleAndUnplacedImagesButKeepsHistory(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 3, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b[3;1H\r\n\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,C=1;AAD//w==\u001b\\"u8);
        processor.Process("\u001b_Ga=t,f=32,i=3,s=1,v=1;AP8A/w==\u001b\\"u8);
        processor.Process("\u001b[2J"u8);
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b_Ga=p,i=2,p=2,C=1\u001b\\\u001b_Ga=p,i=3,p=2,C=1\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
        if (processor is ITerminalViewportScrollSource scroll) scroll.ScrollViewportToTop();
        else screen.ScrollOffset = screen.MaxScrollOffset;
        Assert.Equal(1, Assert.Single(screen.GetKittyPlacements().ToArray()).ImageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelResizeUpdatesPlacementGeometry(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 3, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,c=2,r=2,C=1;/wAA/w==\u001b\\"u8);
        Assert.Equal(16, screen.GetKittyPlacements()[0].WidthPx);
        processor.NotifyResize(8, 3, 128, 96);
        Assert.Equal(32, screen.GetKittyPlacements()[0].WidthPx);
        Assert.Equal(64, screen.GetKittyPlacements()[0].HeightPx);
    }

    private bool CanRun(bool native)
    {
        bool available = !native || GhosttyVtProcessor.IsAvailable() && GhosttyVtHelpers.GetBuildFeatures().KittyGraphics;
        output.WriteLine($"Engine={(native ? "native Ghostty" : "managed")}; available={available}");
        return available;
    }

    private static IVtProcessor Create(bool native, TerminalScreen screen)
        => native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
}
