// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using SkiaSharp;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.Tests;

// Reference decisions:
// Ghostty terminal/render.zig defines actual extra rows and signed viewport Y;
// c/kitty_graphics.zig computeViewportPos retains offscreen coordinates but uses
// (0,0,false) for unresolved pins. Royal extends the image clip/scanner to the
// same captured range, not unbounded requested slots. This is shared Skia host
// integration, not a transplant of Ghostty's viewport-only GPU loops.
// WT renderer/base/renderer.cpp paints ImageSlice using row-relative positions;
// xterm.js addons/addon-image/src/ImageRenderer.ts draws row/column-based tiles
// into a viewport canvas. Both support keeping image and text transforms aligned.
public sealed class TerminalImageOverscanTests
{
    private static readonly TerminalRenderOverscan Request = new(1, 2);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffscreenAnchoredImagesRenderThroughTheSameCapturedRows(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(4, 2, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(4, 2, 40, 20);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,c=1,r=1,z=5,C=1;/wAA/w==\u001b\\a\r\nb\r\nc\r\nd\r\n"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,c=1,r=1,z=5,C=1;AAD//w==\u001b\\e"u8);
        Scroll(processor, screen, 1, Request);
        TerminalKittyImagePlacement[] placements = screen.GetKittyPlacements(Request).ToArray();
        Assert.Equal(2, placements.Length);
        Assert.Equal(-1, placements[0].ViewportRow);
        Assert.Equal(3, placements[1].ViewportRow);
        using SkiaTerminalRenderer renderer = Renderer();
        using SKSurface surface = Surface();
        Draw(surface, renderer, screen, default);
        Assert.Equal(SKColors.Magenta, Pixel(surface, 5));
        Assert.Equal(SKColors.Magenta, Pixel(surface, 45));
        Draw(surface, renderer, screen, Request);
        Assert.Equal(SKColors.Red, Pixel(surface, 5));
        Assert.Equal(SKColors.Blue, Pixel(surface, 45));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaceholderRowsOutsideViewportProjectAndRespectHeldPublication(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(4, 2, 10);
        FrozenClock clock = new();
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen, clock)
            : new BasicVtProcessor(screen, new BasicVtProcessorOptions { TimeProvider = clock });
        processor.NotifyResize(4, 2, 40, 20);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        Write(processor, "\u001b[?2027h\u001b[38;5;1m\U0010EEEE\r\nb\r\nc\r\nd\r\n\U0010EEEE");
        Scroll(processor, screen, 1, Request);
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        TerminalKittyImagePlacement[] old = screen.GetKittyPlacements(Request).ToArray();
        Assert.Equal(new[] { -1, 3 }, old.Select(p => p.ViewportRow));
        using SkiaTerminalRenderer renderer = Renderer();
        using SKSurface surface = Surface();
        Draw(surface, renderer, screen, Request);
        Assert.Equal(SKColors.Red, Pixel(surface, 5));
        Assert.Equal(SKColors.Red, Pixel(surface, 45));
        processor.Process("\u001b[?2026h\u001b[2;1H "u8);
        Assert.Equal(2, screen.GetKittyPlacements(Request).Length);
        processor.Process("\u001b[?2026l"u8);
        TerminalKittyImagePlacement remaining = Assert.Single(screen.GetKittyPlacements(Request).ToArray());
        Assert.Equal(-1, remaining.ViewportRow);
        Assert.Equal(3, old[1].ViewportRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativeChildMayBeVisibleOnlyInsideOverscan(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(4, 2, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(4, 2, 40, 20);
        processor.Process("a\r\nb\r\nc\r\nd\r\ne\u001b[1;1H"u8);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,c=1,r=1,z=5,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=2,s=1,v=1,c=1,r=1,P=1,Q=1,V=1,z=5,C=1;AAD//w==\u001b\\"u8);
        Scroll(processor, screen, 1, Request);
        TerminalKittyImagePlacement[] projected = screen.GetKittyPlacements(Request).ToArray();
        Assert.Equal(new[] { 2, 3 }, projected.Select(p => p.ViewportRow));
        using SkiaTerminalRenderer renderer = Renderer();
        using SKSurface surface = Surface();
        Draw(surface, renderer, screen, Request);
        Assert.Equal(SKColors.Red, Pixel(surface, 35));
        Assert.Equal(SKColors.Blue, Pixel(surface, 45));
    }

    [Theory]
    [InlineData(TerminalRasterImageLayer.BelowBackground)]
    [InlineData(TerminalRasterImageLayer.BelowText)]
    [InlineData(TerminalRasterImageLayer.AboveText)]
    public void RasterImagesClipToActualRowsNotUnfilledRequestedSlots(TerminalRasterImageLayer layer)
    {
        TerminalScreen screen = History();
        screen.ReplaceRasterImage(new(1, TerminalRasterImageProtocol.Sixel, 1, 1, [255, 0, 0, 255]),
            new(1, layer, 0, 0, 0, -20, 10, 30, 0, 0, 1, 1, 10, 10));
        screen.ReplaceRasterImage(new(2, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 255, 255]),
            new(2, layer, 0, 4, 0, 0, 10, 30, 0, 0, 1, 1, 10, 10));
        using SkiaTerminalRenderer renderer = Renderer();
        using SKSurface surface = SKSurface.Create(new SKImageInfo(40, 90));
        // Expose all three layers for the clipping assertion. Opaque cell
        // backgrounds correctly obscure BelowBackground image placements.
        renderer.BackgroundOpacityEnabled = true;
        renderer.BackgroundOpacityCells = true;
        renderer.BackgroundOpacity = 0;
        surface.Canvas.Clear(SKColors.Magenta);
        surface.Canvas.Translate(0, 30);
        renderer.Render(surface.Canvas, screen, new TerminalRenderOverscan(10, 10), forceFullRedraw: true);
        // Only one row above/two below exist: [-10, 40), translated by 30.
        Assert.Equal(SKColors.Magenta, Pixel(surface, 15));
        Assert.Equal(SKColors.Red, Pixel(surface, 25));
        Assert.Equal(SKColors.Blue, Pixel(surface, 65));
        Assert.Equal(SKColors.Magenta, Pixel(surface, 75));
    }

    [Fact]
    public void ViewportAndOverscanQueriesRetainIndependentAllocationFreeCaches()
    {
        TerminalScreen screen = AnchoredHistory();
        for (int i = 0; i < 100; i++) { _ = screen.HasKittyGraphics; _ = screen.GetKittyPlacements(Request); }
        TerminalKittyImagePlacement first = screen.GetKittyPlacements(Request)[0];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { _ = screen.HasKittyGraphics; _ = screen.GetKittyPlacements(Request); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
        Assert.False(screen.HasKittyGraphics);
        Assert.Same(first, screen.GetKittyPlacements(Request)[0]);
        // Different requested padding with the same actual range uses that cache.
        Assert.Same(first, screen.GetKittyPlacements(new(ushort.MaxValue, ushort.MaxValue))[0]);
    }

    [Fact]
    public void CowCopiesAndFailedProjectionDoNotReplacePublishedGeometry()
    {
        TerminalScreen screen = AnchoredHistory();
        TerminalKittyImagePlacement first = screen.GetKittyPlacements(Request)[0];
        TerminalScreen copy = screen.CreateStateCopy();
        Assert.Same(first, copy.GetKittyPlacements(Request)[0]);
        copy.ScrollOffset = 1;
        Assert.Empty(copy.GetKittyPlacements(new(1, 0)).ToArray());
        Assert.Same(first, screen.GetKittyPlacements(Request)[0]);
        screen.MutationCheckpoint = checkpoint =>
        {
            if (checkpoint == SnapshotMutationCheckpoint.KittyProjectionPrepared) throw new OutOfMemoryException();
        };
        Assert.Throws<OutOfMemoryException>(() => { _ = screen.GetKittyPlacements(new(0, 1)); });
        Assert.Same(first, screen.GetKittyPlacements(Request)[0]);
        screen.MutationCheckpoint = null;
        screen.ClearKittyGraphics();
        Assert.Empty(screen.GetKittyPlacements(Request).ToArray());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void ExtremeRelativeCoordinatesDoNotWrapIntoTheVisibleRange(long offset)
    {
        TerminalScreen screen = History();
        TerminalScreenAnchor anchor = screen.CreateAnchor(4, 1);
        screen.ReplaceAnchoredKittyGraphics([new(1, 1, 1, [255, 0, 0, 255])],
            [new(anchor, offset, offset, uint.MaxValue, uint.MaxValue, Geometry(1))], [], [], 10, 10);
        Assert.Empty(screen.GetKittyPlacements(Request).ToArray());
    }

    [Theory]
    [InlineData(-1, 1, 0, 1, true)]
    [InlineData(-2, 1, 0, 1, false)]
    [InlineData(-2, 2, 0, 1, true)]
    [InlineData(2, 1, 0, 1, true)]
    [InlineData(3, 1, 0, 1, true)]
    [InlineData(4, 1, 0, 1, false)]
    [InlineData(0, 1, 0, 1, false)]
    [InlineData(-1, 1, -1, 1, false)]
    [InlineData(-1, 1, -1, 2, true)]
    [InlineData(-1, 1, 4, 1, false)]
    [InlineData(-1, 0, 0, 1, false)]
    public void NativeOffscreenCullingDistinguishesUnresolvedPins(int row, int height, int column, int width, bool expected)
    {
        GhosttyKittyGraphicsPlacementRenderInfo info = new()
        {
            ViewportRow = row, ViewportColumn = column, GridRows = (uint)height, GridColumns = (uint)width,
        };
        Assert.Equal(expected, GhosttyVtProcessor.IsKittyRenderInfoVisible(info, 4, 2, Request));
        Assert.False(GhosttyVtProcessor.IsKittyRenderInfoVisible(info, 4, 2, default));
    }

    private static TerminalScreen History()
    {
        TerminalScreen screen = new(4, 2, 10);
        for (int i = 0; i < 3; i++) screen.AddRow();
        screen.ScrollOffset = 2;
        return screen;
    }

    private static TerminalScreen AnchoredHistory()
    {
        TerminalScreen screen = History();
        screen.ReplaceAnchoredKittyGraphics([new(1, 1, 1, [255, 0, 0, 255]), new(2, 1, 1, [0, 0, 255, 255])],
            [new(screen.CreateAnchor(0, 0), 0, 0, 1, 1, Geometry(1)),
             new(screen.CreateAnchor(4, 0), 0, 0, 1, 1, Geometry(2))], [], [], 10, 10);
        return screen;
    }

    private static TerminalKittyImagePlacement Geometry(int id)
        => new(id, TerminalKittyImageLayer.AboveText, 0, 0, 0, 0, 10, 10, 0, 0, 1, 1, 10, 10);
    private static bool CanRun(bool native)
        => !native || GhosttyVtProcessor.IsAvailable() && GhosttyVtHelpers.GetBuildFeatures().KittyGraphics;
    private static IVtProcessor Create(bool native, TerminalScreen screen)
        => native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    private static void Write(IVtProcessor processor, string text) => processor.Process(Encoding.UTF8.GetBytes(text));
    private static void Scroll(IVtProcessor processor, TerminalScreen screen, int top, TerminalRenderOverscan request)
    {
        if (processor is GhosttyVtProcessor native)
        {
            native.RenderOverscan = request;
            native.SetViewportOffsetRows((ulong)top);
        }
        else screen.ScrollOffset = screen.MaxScrollOffset - top;
    }
    private static SkiaTerminalRenderer Renderer()
    {
        SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(10, 10);
        return renderer;
    }
    private static SKSurface Surface() => SKSurface.Create(new SKImageInfo(40, 50));
    private static void Draw(SKSurface surface, SkiaTerminalRenderer renderer, TerminalScreen screen, TerminalRenderOverscan request)
    {
        surface.Canvas.Clear(SKColors.Magenta);
        surface.Canvas.Save();
        surface.Canvas.Translate(0, 10);
        renderer.Render(surface.Canvas, screen, request, forceFullRedraw: true);
        surface.Canvas.Restore();
    }
    private static SKColor Pixel(SKSurface surface, int y)
    {
        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        return pixels.GetPixelColor(5, y);
    }
    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }
}
