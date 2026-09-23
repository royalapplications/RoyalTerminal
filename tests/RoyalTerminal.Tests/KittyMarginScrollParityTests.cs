// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class KittyMarginScrollParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiRowScrollClipsSourceOnceAndKeepsStraddlingPlacementStationary(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 8);
        Upload(processor, 2, 8);
        Write(processor, "\u001b[2;1H\u001b_Ga=p,i=1,p=1,c=1,r=5,C=1\u001b\\" +
            "\u001b[6;3H\u001b_Ga=p,i=2,p=1,c=1,r=2,C=1\u001b\\\u001b[2;6r\u001b[2S");
        TerminalKittyImagePlacement clipped = Find(screen, 1);
        Assert.Equal((1, 3, 5, 30), (clipped.ViewportRow, clipped.SourceY, clipped.SourceHeight, clipped.HeightPx));
        Assert.Equal(5, Find(screen, 2).ViewportRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReverseIndexClipsBottomAndFullClipKeepsImageForRedisplay(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 8);
        Write(processor, "\u001b[5;1H\u001b_Ga=p,i=1,p=1,c=1,r=2,C=1\u001b\\\u001b[2;6r\u001b[2;1H\u001bM");
        TerminalKittyImagePlacement clipped = Find(screen, 1);
        Assert.Equal((5, 0, 4, 10), (clipped.ViewportRow, clipped.SourceY, clipped.SourceHeight, clipped.HeightPx));
        Write(processor, "\u001bM");
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        Write(processor, "\u001b[3;1H\u001b_Ga=p,i=1,p=2,c=1,r=2,C=1\u001b\\");
        Assert.Equal(8, Find(screen, 1).SourceHeight);
    }

    private static IVtProcessor Create(bool native, TerminalScreen screen)
        => native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void TopOriginScrollPreservesHistoryAndStatusRows(bool native, bool index, bool alternate)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 6, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 6, 80, 60);
        if (alternate) Write(processor, "\u001b[?1049h");
        Write(processor, "A\u001b[2;1HB\u001b[3;1HC\u001b[4;1HD\u001b[5;1HE\u001b[6;1HF");
        Upload(processor, 1, 8);
        Upload(processor, 2, 8);
        Upload(processor, 3, 8);
        Write(processor, "\u001b[3;1H\u001b_Ga=p,i=1,p=1,c=1,r=1,C=1\u001b\\" +
            "\u001b[4;2H\u001b_Ga=p,i=2,p=1,c=1,r=2,C=1\u001b\\" +
            "\u001b[6;3H\u001b_Ga=p,i=3,p=1,c=1,r=1,C=1\u001b\\\u001b[1;4r\u001b[4;1H");
        Write(processor, index ? "\u001bD\u001bD" : "\u001b[2S");
        Assert.Equal((ulong)(alternate ? 6 : 8), processor is GhosttyVtProcessor ghostty
            ? ghostty.ViewportScrollState.TotalRows : (ulong)screen.TotalRows);
        Assert.Equal('C', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('D', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        Assert.Equal('E', screen.GetViewportRow(4).ReadOnlyCells[0].Codepoint);
        Assert.Equal('F', screen.GetViewportRow(5).ReadOnlyCells[0].Codepoint);
        if (!alternate)
        {
            TerminalScreen history = screen;
            if (processor is GhosttyVtProcessor nativeProcessor)
                Assert.True(nativeProcessor.TryCreateScreenSnapshot(0, 2, 0, out history));
            Assert.Equal('A', history.GetRow(0).ReadOnlyCells[0].Codepoint);
            Assert.Equal('B', history.GetRow(1).ReadOnlyCells[0].Codepoint);
        }
        Assert.Equal(0, Find(screen, 1).ViewportRow);
        Assert.Equal(3, Find(screen, 2).ViewportRow);
        Assert.Equal(5, Find(screen, 3).ViewportRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopOriginHeldScrollPreservesStraddlingImageAndBackground(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 6, 1);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 6, 80, 60);
        Upload(processor, 1, 8);
        Write(processor, "\u001b[1;1HA\u001b_Ga=p,i=1,p=1,c=1,r=5,C=1\u001b\\\u001b[1;4r");
        TerminalKittyImagePlacement before = Find(screen, 1);
        Write(processor, "\u001b[?2026h\u001b[48;2;10;20;30m\u001b[4S");
        Assert.Equal(before, Find(screen, 1));
        Assert.Equal('A', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Write(processor, "\u001b[?2026l");
        Assert.Equal(0, Find(screen, 1).ViewportRow);
        Assert.Equal(8, Find(screen, 1).SourceHeight);
        Assert.Equal(0xFF0A141Eu, screen.GetViewportRow(3).ReadOnlyCells[0].Background);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineInsertAndDeleteKeepImagesStationaryEvenIfTheirTextAnchorIsDiscarded(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 8);
        Write(processor, "\u001b[3;1H\u001b_Ga=p,i=1,p=1,c=1,r=1,C=1\u001b\\\u001b[2;6r\u001b[2;1H\u001b[4M");
        Assert.Equal(2, Find(screen, 1).ViewportRow);
        Write(processor, "\u001b[5L");
        Assert.Equal(2, Find(screen, 1).ViewportRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullyClippedRootRemovesRelativeDescendantsButNotImageData(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 8);
        Upload(processor, 2, 8);
        Write(processor, "\u001b[2;1H\u001b_Ga=p,i=1,p=1,c=1,r=1,C=1\u001b\\" +
            "\u001b_Ga=p,i=2,p=1,P=1,Q=1,H=1,V=1,c=1,r=1,C=1\u001b\\\u001b[2;6r\u001b[999S");
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        Write(processor, "\u001b_Ga=p,i=2,p=2,c=1,r=1,C=1\u001b\\");
        Assert.Equal(8, Find(screen, 2).SourceHeight);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeSizeClippingRetainsPixelOffsetAndExactVisibleSource(bool native, bool down)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 25);
        Write(processor, $"\u001b[{(down ? 4 : 2)};1H\u001b_Ga=p,i=1,p=1,Y=5,C=1\u001b\\\u001b[2;6r\u001b[{(down ? "T" : "S")}");
        TerminalKittyImagePlacement clipped = Find(screen, 1);
        Assert.Equal((down ? 4 : 1, down ? 0 : 10, 15, 15, 5),
            (clipped.ViewportRow, clipped.SourceY, clipped.SourceHeight, clipped.HeightPx, clipped.YOffsetPx));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronizedOutputKeepsOriginalProjectionUntilClippingIsPublished(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 8);
        Write(processor, "\u001b[2;1H\u001b_Ga=p,i=1,p=1,c=1,r=4,C=1\u001b\\\u001b[2;6r");
        TerminalKittyImagePlacement before = Find(screen, 1);
        Write(processor, "\u001b[?2026h\u001b[S");
        Assert.Equal(before, Find(screen, 1));
        Write(processor, "\u001b[?2026l");
        Assert.Equal((2, 6), (Find(screen, 1).SourceY, Find(screen, 1).SourceHeight));
        Assert.Equal(8, before.SourceHeight);
    }

    [Fact]
    public void WarmInPlaceScrollOfUnclippedImageDoesNotAllocate()
    {
        TerminalScreen screen = new(8, 7, 20);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 7, 80, 70);
        Upload(processor, 1, 8);
        Write(processor, "\u001b[4;1H\u001b_Ga=p,i=1,p=1,c=1,r=1,C=1\u001b\\\u001b[2;6r");
        processor.Process("\u001b[S\u001b[T"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) processor.Process("\u001b[S\u001b[T"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(3, Find(screen, 1).ViewportRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreportedPixelMetricsDoNotAssumePlacementFitsMargins(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 7, 20);
        using IVtProcessor processor = Create(native, screen);
        Upload(processor, 1, 8);
        Write(processor, "\u001b[2;1H\u001b_Ga=p,i=1,p=1,C=1\u001b\\\u001b[2;6r\u001b[S");
        processor.NotifyResize(8, 7, 80, 70);
        Assert.Equal((1, 0, 8), (Find(screen, 1).ViewportRow, Find(screen, 1).SourceY, Find(screen, 1).SourceHeight));
    }

    private static void Upload(IVtProcessor processor, int id, int height)
    {
        byte[] pixels = new byte[height * 4];
        for (int i = 0; i < height; i++) { pixels[i * 4] = (byte)(20 + i * 20); pixels[i * 4 + 3] = 255; }
        Write(processor, $"\u001b_Ga=t,f=32,i={id},s=1,v={height};{Convert.ToBase64String(pixels)}\u001b\\");
    }

    private static TerminalKittyImagePlacement Find(TerminalScreen screen, int id)
    {
        foreach (TerminalKittyImagePlacement placement in screen.GetKittyPlacements())
            if (placement.ImageId == id) return placement;
        throw new Xunit.Sdk.XunitException($"No placement for image {id}");
    }

    private static void Write(IVtProcessor processor, string text) => processor.Process(Encoding.UTF8.GetBytes(text));

    private bool CanRun(bool native)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native requested: {native}; available: {available}");
        return !native || available;
    }
}
