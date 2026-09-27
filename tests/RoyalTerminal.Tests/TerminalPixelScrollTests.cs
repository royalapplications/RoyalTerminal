// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty render.zig explicitly supports a fractional translation plus extra
// rows, but keeps cursor/preedit restricted to viewport rows. xterm Viewport.ts
// retains fractional scrollbar positions then rounds the terminal row; WT
// Terminal::UserScrollViewport takes an integer. Royal's opt-in path deliberately
// uses floor + a shared subrow transform; the default row-scrolling path stays.
public sealed class TerminalPixelScrollTests
{
    [Theory]
    [InlineData(0, 30, 3, 0, 0)]
    [InlineData(15, 30, 3, 1, 0.5)]
    [InlineData(30, 30, 3, 3, 0)]
    [InlineData(40, 30, 3, 3, 0)]
    [InlineData(-5, 30, 3, 0, 0)]
    [InlineData(11, 22, 3, 1, 0.5)]
    [InlineData(10, 0, 3, 0, 0)]
    [InlineData(double.NaN, 30, 3, 0, 0)]
    [InlineData(10, double.PositiveInfinity, 3, 0, 0)]
    public void PixelRangeMapsToRowAndPhase(double offset, double maximum, int rows, int row, double fraction)
    {
        Assert.Equal(new TerminalViewportScrollPosition((ulong)row, fraction),
            TerminalViewportScrollPosition.FromPixels(offset, maximum, (ulong)rows));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFractionsAreRejected(double fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalViewportScrollPosition(0, fraction));
        Assert.Throws<ArgumentOutOfRangeException>(() => History().RenderScrollFraction = fraction);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(4.9, 0)]
    [InlineData(5, 1)]
    [InlineData(14.9, 1)]
    [InlineData(15, 2)]
    [InlineData(25, 2)]
    public void InputUsesSameFractionAndIncludesPartialBottomRow(double y, int row)
    {
        TerminalScreen screen = History();
        screen.RenderScrollFraction = 0.5;
        Assert.Equal(row, screen.GetRenderRowAtPixel(y, 10));
        Assert.Equal(new TerminalRenderOverscan(0, 1), screen.RenderScrollOverscan);
    }

    [Fact]
    public void MissingBottomRowsSuppressPhaseAndResizeClearsIt()
    {
        TerminalScreen screen = History();
        screen.RenderScrollFraction = 0.5;
        screen.ScrollOffset = 0;
        Assert.Equal(0, screen.RenderScrollFraction);
        Assert.Equal(default, screen.RenderScrollOverscan);
        screen.ScrollOffset = 1;
        Assert.Equal(0.5, screen.RenderScrollFraction);
        screen.Resize(5, 2, reflowOnResize: false);
        Assert.Equal(0, screen.RenderScrollFraction);
    }

    [Fact]
    public void TerminalPublicationKeepsTheReceivingPresentationPhase()
    {
        TerminalScreen screen = History();
        screen.RenderScrollFraction = 0.25;
        TerminalScreen staged = screen.CreateStateCopy();
        screen.RenderScrollFraction = 0.75;
        screen.AdoptStateFrom(staged);
        Assert.Equal(0.75, screen.RenderScrollFraction);
        screen.ClearAll();
        Assert.Equal(0, screen.RenderScrollFraction);
    }

    [Fact]
    public void ScrollDataCombinesOffsetsBeforeRounding()
    {
        TerminalScrollData data = new() { CellHeight = 10, Extent = 50, Viewport = 20, Offset = 15 };
        Assert.Equal(2, data.ViewportYToRow(6));
    }

    [Fact]
    public void DefaultRendererTranslatesCellsAndImagesWithoutAGap()
    {
        TerminalScreen screen = History();
        screen.RenderScrollFraction = 0.5;
        SKColor[] colors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow, SKColors.Cyan];
        for (int row = 0; row < screen.TotalRows; row++)
            foreach (ref TerminalCell cell in screen.GetRow(row).Cells)
            {
                cell.Background = (uint)colors[row];
                cell.HasBackground = true;
            }
        screen.ReplaceRasterImage(new(1, TerminalRasterImageProtocol.Sixel, 1, 1, [255, 0, 255, 255]),
            new(1, TerminalRasterImageLayer.AboveText, 1, 3, 0, 0, 10, 10, 0, 0, 1, 1, 10, 10));
        using SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(10, 10);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(40, 20));
        renderer.RenderFull(surface.Canvas, screen);
        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        Assert.Equal(SKColors.Lime, pixels.GetPixelColor(5, 2));
        Assert.Equal(SKColors.Blue, pixels.GetPixelColor(5, 7));
        Assert.Equal(SKColors.Yellow, pixels.GetPixelColor(5, 17));
        Assert.Equal(SKColors.Magenta, pixels.GetPixelColor(15, 17));
    }

    [Fact]
    public void NativePositionPublishesAtomicallyAndFractionOnlyMotionReusesStorage()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = new(screen, new FrozenClock());
        processor.Process("a\r\nb\r\nc\r\nd\r\ne"u8);
        processor.SetViewportScrollPosition(new(1, 0.25));
        Assert.Equal(new TerminalViewportScrollPosition(1, 0.25), processor.PublishedViewportPosition);
        Assert.Equal(3, screen.GetRenderViewport(screen.RenderScrollOverscan).Count);
        TerminalRow row = screen.GetViewportRow(0);
        for (int index = 0; index < 100; index++) processor.SetViewportScrollPosition(new(1, 0.5));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++) processor.SetViewportScrollPosition(new(1, index % 2 == 0 ? 0.25 : 0.5));
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
        Assert.Same(row, screen.GetViewportRow(0));
        processor.Process("\u001b[?2026h"u8);
        processor.SetViewportScrollPosition(new(2, 0.75));
        Assert.Equal(new TerminalViewportScrollPosition(1, 0.5), processor.PublishedViewportPosition);
        Assert.Equal('b', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(new TerminalViewportScrollPosition(2, 0.75), processor.PublishedViewportPosition);
        Assert.Equal('c', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        processor.ScrollViewportToBottom();
        Assert.Equal(0, screen.RenderScrollFraction);
        Assert.Null(screen.ExternalRenderRows);
    }

    [Fact]
    public void NativeIntegerScrollingResizeAndDisposeDropFraction()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = new(screen);
        processor.Process("a\r\nb\r\nc\r\nd\r\ne"u8);
        processor.SetViewportScrollPosition(new(1, 0.5));
        processor.SetViewportOffsetRows(1);
        Assert.Equal(0, screen.RenderScrollFraction);
        processor.SetViewportScrollPosition(new(1, 0.5));
        processor.NotifyResize(5, 2);
        Assert.Equal(0, screen.RenderScrollFraction);
        processor.SetViewportScrollPosition(new(1, 0.5));
        processor.Dispose();
        Assert.Equal(0, screen.RenderScrollFraction);
    }

    private static TerminalScreen History()
    {
        TerminalScreen screen = new(4, 2, 10);
        for (int index = 0; index < 3; index++) screen.AddRow();
        screen.ScrollOffset = 2;
        return screen;
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }
}
