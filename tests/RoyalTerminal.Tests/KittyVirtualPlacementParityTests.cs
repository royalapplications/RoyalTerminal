// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class KittyVirtualPlacementParityTests(ITestOutputHelper output)
{
    private const string P = "\U0010EEEE";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VirtualPlacementProjectsExistingTextAndRendersActualPixels(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        Write(processor, "\u001b[?2027h\u001b[2;4H\u001b[38;5;42m" + P + "\u0305\u0305" + P + "\u0305\u030D");
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b_Ga=T,f=32,i=42,p=7,U=1,s=2,v=1,c=2,r=1,z=99;/wAA/wAA//8=\u001b\\"u8);
        TerminalKittyImagePlacement placement = Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal((3, 1, 20, 10, -1), (placement.ViewportColumn, placement.ViewportRow, placement.WidthPx, placement.HeightPx, placement.ZIndex));
        Assert.Equal(TerminalKittyImageLayer.BelowText, placement.Layer);

        using SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(10, 10);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(80, 40));
        renderer.RenderFull(surface.Canvas, screen);
        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixels = snapshot.PeekPixels();
        Assert.Equal(SKColors.Red, pixels.GetPixelColor(34, 15));
        Assert.Equal(SKColors.Blue, pixels.GetPixelColor(45, 15));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultTargetPrefersExternalThenLowestIdAndExplicitUnderlineSelectsExactPlacement(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=42,U=1,s=2,v=1,c=1,r=1;/wAA/wAA//8=\u001b\\"u8);
        processor.Process("\u001b_Ga=p,i=42,p=9,U=1,c=2,r=1\u001b\\"u8);
        Write(processor, "\u001b[?2027h\u001b[38;5;42m" + P + P);
        Assert.Equal(20, Assert.Single(screen.GetKittyPlacements().ToArray()).WidthPx);
        processor.Process("\u001b_Ga=p,i=42,p=3,U=1,c=1,r=1\u001b\\"u8);
        Assert.Equal(10, Assert.Single(screen.GetKittyPlacements().ToArray()).WidthPx);
        Write(processor, "\r\u001b[58;2;0;0;9m" + P + P);
        Assert.Equal(20, Assert.Single(screen.GetKittyPlacements().ToArray()).WidthPx);
        Write(processor, "\r\u001b[58;5;3m" + P + P);
        Assert.Equal(10, Assert.Single(screen.GetKittyPlacements().ToArray()).WidthPx);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativeChildrenUseIndependentMinimumCoordinatesOfVisibleParentRuns(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=9,s=1,v=1,c=1,r=1,P=1,Q=7,H=2,V=1,z=5,C=1;AAD//w==\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
        Write(processor, "\u001b[?2027h\u001b[38;5;1m\u001b[1;4H" + P + "\u001b[2;2H" + P);
        Assert.Equal(3, screen.GetKittyPlacements().Length);
        TerminalKittyImagePlacement child = screen.GetKittyPlacements()[2];
        Assert.Equal((2, 3, 1, 5), (child.ImageId, child.ViewportColumn, child.ViewportRow, child.ZIndex));
        // Remove the upper run: the surviving parent origin becomes (1, 1).
        processor.Process("\u001b[1;4H \u001b[0m"u8);
        child = screen.GetKittyPlacements()[1];
        Assert.Equal((3, 2), (child.ViewportColumn, child.ViewportRow));
        processor.Process("\u001b[2;2H "u8);
        Assert.False(screen.HasKittyGraphics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoldDeletionResizeAndAlternateScreenRespectPublishedPlaceholderState(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        Write(processor, "\u001b[38;5;1m" + P);
        TerminalKittyImagePlacement held = Assert.Single(screen.GetKittyPlacements().ToArray());
        processor.Process("\u001b[?2026h\r "u8);
        Assert.Single(screen.GetKittyPlacements().ToArray());
        processor.Process("\u001b[?2026l"u8);
        Assert.False(screen.HasKittyGraphics);
        Assert.Equal(10, held.WidthPx);
        Write(processor, "\r" + P);
        processor.NotifyResize(8, 4, 160, 80);
        Assert.Equal(20, screen.GetKittyPlacements()[0].WidthPx);
        processor.Process("\u001b[?1049h"u8);
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b[?1049l"u8);
        Assert.True(screen.HasKittyGraphics);
        processor.Process("\u001b_Ga=d,d=i,i=1,p=7\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
    }

    [Fact]
    public void ManagedScrollbackNavigationAndUnchangedProjectionAreAllocationFree()
    {
        TerminalScreen screen = new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 3, 80, 30);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        Write(processor, "\u001b[38;5;1m" + P + "\u001b[3;1H\r\n");
        Assert.False(screen.HasKittyGraphics);
        screen.ScrollOffset = 1;
        Assert.Equal(0, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportRow);
        for (int i = 0; i < 100; i++) screen.GetKittyPlacements();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++) screen.GetKittyPlacements();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HighIdPlaceholdersReuseProjectionAndPixelsAcrossUnrelatedText(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=33554474,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        Write(processor, "\u001b[?2027h\u001b[38;5;42m" + P + "\u0305\u0305\u030E");
        TerminalKittyImagePlacement placement = Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(33554474, placement.ImageId);
        Assert.True(screen.TryGetKittyImageSource(33554474, out var original));
        processor.Process("\u001b[3;1Habc"u8);
        Assert.Same(placement, screen.GetKittyPlacements()[0]);
        Assert.True(screen.TryGetKittyImageSource(33554474, out var current));
        Assert.Same(original, current);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++) screen.GetKittyPlacements();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitPlaceholderIdCanTargetAnOrdinaryPlacement(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=7,s=1,v=1,c=1,r=1,C=1,z=5;AAD//w==\u001b\\"u8);
        Write(processor, "\u001b[2;5H\u001b[38;5;2;58;5;7m" + P);
        Assert.Equal(2, screen.GetKittyPlacements().Length);
        TerminalKittyImagePlacement fragment = screen.GetKittyPlacements()[0];
        Assert.Equal((2, 4, 1, -1), (fragment.ImageId, fragment.ViewportColumn, fragment.ViewportRow, fragment.ZIndex));
        // Removing the last virtual placement disables the placeholder scan, not the ordinary image.
        processor.Process("\u001b_Ga=d,d=i,i=1\u001b\\"u8);
        TerminalKittyImagePlacement anchored = Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(5, anchored.ZIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollEraseAndResetDoNotLeaveStaleVirtualRecipes(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 3, 10);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 3, 80, 30);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        Write(processor, "\u001b[38;5;1m" + P + "\u001b[3;1H\r\n");
        Assert.False(screen.HasKittyGraphics);
        if (native) ((ITerminalViewportScrollSource)processor).ScrollViewportToTop();
        else screen.ScrollOffset = 1;
        Assert.Equal(0, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportRow);
        if (native) ((ITerminalViewportScrollSource)processor).ScrollViewportToBottom();
        else screen.ScrollOffset = 0;
        processor.Process("\u001b[2J\u001b[H"u8);
        Write(processor, P);
        Assert.True(screen.HasKittyGraphics); // ED2 erases text, not the virtual definition.
        processor.Process("\u001bc"u8);
        Write(processor, "\u001b[38;5;1m" + P);
        Assert.False(screen.HasKittyGraphics);
    }

    private bool CanRun(bool native)
    {
        bool available = !native || GhosttyVtProcessor.IsAvailable() && GhosttyVtHelpers.GetBuildFeatures().KittyGraphics;
        output.WriteLine($"Native requested={native}; available={available}");
        return available;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativeChainsRemainPositionedWhenRootFragmentIsEntirelyPadding(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = Create(native, screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=2,v=1,c=1,r=4;/wAA/wAA//8=\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=9,s=1,v=1,c=1,r=1,P=1,Q=7,H=1,V=1,z=1,C=1;AAD//w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=3,p=11,s=1,v=1,c=1,r=1,P=2,Q=9,H=1,V=-1,z=2,C=1;AP8A/w==\u001b\\"u8);
        Write(processor, "\u001b[2;4H\u001b[38;5;1m" + P);
        ReadOnlySpan<TerminalKittyImagePlacement> placements = screen.GetKittyPlacements();
        Assert.Equal(2, placements.Length);
        Assert.Equal((2, 4, 2), (placements[0].ImageId, placements[0].ViewportColumn, placements[0].ViewportRow));
        Assert.Equal((3, 5, 1), (placements[1].ImageId, placements[1].ViewportColumn, placements[1].ViewportRow));
        processor.Process("\u001b_Ga=d,d=i,i=1,p=7\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VirtualImagesAdvanceAnimationWithoutNewText(bool native)
    {
        if (!CanRun(native)) return;
        TestClock clock = new();
        TerminalScreen screen = new(8, 4, 20);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen, clock) : new BasicVtProcessor(screen, new() { TimeProvider = clock });
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=f,f=32,i=1,s=1,v=1,z=40;AAD//w==\u001b\\"u8);
        Write(processor, "\u001b[38;5;1m" + P);
        processor.Process("\u001b_Ga=a,i=1,r=1,z=40,s=3\u001b\\"u8);
        Assert.True(screen.TryGetKittyImageSource(1, out var before));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, before!.RgbaPixels);
        clock.Advance(TimeSpan.FromMilliseconds(40));
        Assert.True(((ITerminalTimedRefreshSource)processor).RefreshTimedState());
        Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.True(screen.TryGetKittyImageSource(1, out var after));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, after!.RgbaPixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, before.RgbaPixels);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private static IVtProcessor Create(bool native, TerminalScreen screen) => native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    private static void Write(IVtProcessor processor, string text) => processor.Process(Encoding.UTF8.GetBytes(text));
}
