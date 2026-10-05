// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty computes placement positions from tracked pins and the current viewport
/// (kitty_graphics.zig computeViewportPos). xterm's image renderer likewise resolves
/// buffer cells through ydisp; Windows Terminal ignores Kitty APC sequences.
/// </summary>
public sealed class ManagedKittyViewportTests
{
    private static ReadOnlySpan<byte> TwoRowImage =>
        "\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,c=1,r=2,C=1;/wAA/w==\u001b\\"u8;

    [Fact]
    public void OutputAndScrollback_ReprojectWithoutAnotherGraphicsCommand()
    {
        TerminalScreen screen = new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process(TwoRowImage);
        TerminalKittyImagePlacement original = Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(0, original.ViewportRow);

        processor.Process("\u001b[3;1H\r\n"u8);
        Assert.Equal(-1, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportRow);
        processor.Process("\r\n"u8);
        Assert.False(screen.HasKittyGraphics);

        screen.ScrollOffset = 2;
        Assert.Equal(0, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportRow);
        screen.ScrollOffset = 1;
        Assert.Equal(-1, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportRow);
        screen.ScrollOffset = 0;
        Assert.False(screen.HasKittyGraphics);
        Assert.Equal(0, original.ViewportRow);
    }

    [Fact]
    public void SynchronizedOutput_PublishesAnchorMovementOnlyWhenReleased()
    {
        TerminalScreen screen = new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process(TwoRowImage);
        Assert.Equal(0, screen.GetKittyPlacements()[0].ViewportRow);

        processor.Process("\u001b[?2026h\u001b[3;1H\r\n"u8);
        Assert.Equal(0, screen.GetKittyPlacements()[0].ViewportRow);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(-1, screen.GetKittyPlacements()[0].ViewportRow);
    }

    [Fact]
    public void InsertedLinesKeepImageStationaryAndPrunedHistoryInvalidatesProjection()
    {
        TerminalScreen screen = new(8, 3, 0);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process("\u001b[2;1H"u8);
        processor.Process(TwoRowImage);
        Assert.Equal(1, screen.GetKittyPlacements()[0].ViewportRow);
        processor.Process("\u001b[1;1H\u001b[L"u8);
        // Ghostty keeps image pins stationary for IL/DL; ordinary scrolling moves them.
        Assert.Equal(1, screen.GetKittyPlacements()[0].ViewportRow);
        processor.Process("\u001b[3;1H\r\n"u8);
        Assert.Equal(0, screen.GetKittyPlacements()[0].ViewportRow);
        processor.Process("\r\n\r\n"u8);
        Assert.False(screen.HasKittyGraphics);
    }

    [Fact]
    public void RelativePlacement_UsesItsRootAfterTextScrolls()
    {
        TerminalScreen screen = new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process(TwoRowImage);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,P=1,Q=1,V=2,C=1;AAD//w==\u001b\\"u8);
        Assert.Equal(2, screen.GetKittyPlacements()[1].ViewportRow);
        processor.Process("\u001b[3;1H\r\n\r\n"u8);
        TerminalKittyImagePlacement child = Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(2, child.ImageId);
        Assert.Equal(0, child.ViewportRow);
    }

    [Fact]
    public void UnchangedProjection_ReusesPlacementsWithoutAllocations()
    {
        TerminalScreen screen = new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 3, 64, 48);
        processor.Process(TwoRowImage);
        TerminalKittyImagePlacement initial = screen.GetKittyPlacements()[0];
        for (int i = 0; i < 100; i++) _ = screen.GetKittyPlacements();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = screen.GetKittyPlacements();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(initial, screen.GetKittyPlacements()[0]);
    }

    [Fact]
    public void PlacementViewportPositions_MatchNativeGhostty_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        TerminalScreen managedScreen = new(8, 3, 10);
        TerminalScreen nativeScreen = new(8, 3, 10);
        using BasicVtProcessor managed = new(managedScreen);
        using GhosttyVtProcessor native = new(nativeScreen);
        managed.NotifyResize(8, 3, 64, 48);
        native.NotifyResize(8, 3, 64, 48);
        managed.Process(TwoRowImage);
        native.Process(TwoRowImage);
        AssertSamePlacement(managedScreen, nativeScreen);
        managed.Process("\u001b[3;1H\r\n"u8);
        native.Process("\u001b[3;1H\r\n"u8);
        AssertSamePlacement(managedScreen, nativeScreen);
        managed.Process("\r\n"u8);
        native.Process("\r\n"u8);
        AssertSamePlacement(managedScreen, nativeScreen);
        managedScreen.ScrollOffset = 2;
        native.ScrollViewportToTop();
        AssertSamePlacement(managedScreen, nativeScreen);
        managedScreen.ScrollOffset = 0;
        native.ScrollViewportToBottom();
        AssertSamePlacement(managedScreen, nativeScreen);
    }

    private static void AssertSamePlacement(TerminalScreen managed, TerminalScreen native)
    {
        TerminalKittyImagePlacement[] actual = managed.GetKittyPlacements().ToArray();
        TerminalKittyImagePlacement[] expected = native.GetKittyPlacements().ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.Equal(expected[i].ImageId, actual[i].ImageId);
            Assert.Equal(expected[i].ViewportColumn, actual[i].ViewportColumn);
            Assert.Equal(expected[i].ViewportRow, actual[i].ViewportRow);
            Assert.Equal(expected[i].WidthPx, actual[i].WidthPx);
            Assert.Equal(expected[i].HeightPx, actual[i].HeightPx);
        }
    }
}
