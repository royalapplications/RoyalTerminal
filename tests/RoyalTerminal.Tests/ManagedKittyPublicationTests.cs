// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyPublicationTests(ITestOutputHelper output)
{
    // Ghostty renderer/image.zig retains its placement buffer across updates.
    // Keep that allocation property without sharing mutable scratch with held screens.
    // Like WT viewport invalidation and xterm.js RenderService, geometry changes
    // must still invalidate even when pixel dimensions or the image ID are unchanged.
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 64)]
    [InlineData(true, 1)]
    [InlineData(true, 64)]
    public void WarmAnimationPublicationDoesNotAllocatePlacementSizedBuffers(bool virtualPlacement, int count)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, count);
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        ManagedKittyGraphicsCommand first = Command("a=a,c=1"), second = Command("a=a,c=2");
        for (int i = 0; i < 100; i++) PublishFrame(i);
        const int iterations = 1000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) PublishFrame(i);
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"virtual={virtualPlacement}, placements={count}, frames={iterations}, allocated={allocated}, ms={milliseconds:F3}");
        // Warmed immutable frames retain their image-source wrapper as well as
        // sharing unchanged geometry. Playback allocates neither on a revisit.
        Assert.Equal(0, allocated);

        void PublishFrame(int frame)
        {
            image.Animation.ApplyControl((frame & 1) == 0 ? first : second);
            store.MarkContentChanged(image);
            store.Publish(screen, 8, 16);
            _ = screen.GetKittyPlacements();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelUpdatesRetainGeometryAndDoNotChangeHeldScreen(bool virtualPlacement)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, 3);
        ReadOnlySpan<TerminalKittyImagePlacement> before = screen.GetKittyPlacements();
        Assert.NotEmpty(before.ToArray());
        TerminalScreen held = screen.CreateStateCopy();
        Assert.True(held.TryGetKittyImageSource(1, out TerminalKittyImageSource? original));
        SelectSecondFrame(screen, store);
        Assert.True(before.Overlaps(screen.GetKittyPlacements()));
        Assert.True(before.Overlaps(held.GetKittyPlacements()));
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? current));
        Assert.NotSame(original, current);
        Assert.Equal(1, original!.RgbaPixels[0]);
        Assert.Equal(9, current!.RgbaPixels[0]);
        Assert.True(held.TryGetKittyImageSource(1, out TerminalKittyImageSource? stillHeld));
        Assert.Same(original, stillHeld);
    }

    [Fact]
    public void PixelOnlyPublicationStillProjectsMovedAnchors()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(false, 1);
        ReadOnlySpan<TerminalKittyImagePlacement> before = screen.GetKittyPlacements();
        ManagedKittyGraphicsStore.Placement placement = store.Placements.Single().Value;
        Assert.True(screen.MoveAnchor(placement.Anchor!, 4, 7));
        SelectSecondFrame(screen, store);
        Assert.Equal(0, before[0].ViewportColumn);
        Assert.Equal(7, screen.GetKittyPlacements()[0].ViewportColumn);
        Assert.Equal(4, screen.GetKittyPlacements()[0].ViewportRow);
    }

    [Fact]
    public void PixelOnlyPublicationStillScansMovedPlaceholderCells()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(true, 1);
        ReadOnlySpan<TerminalKittyImagePlacement> before = screen.GetKittyPlacements();
        TerminalCell cell = screen.GetViewportRow(0).ReadOnlyCells[0];
        screen.GetViewportRow(0).Cells[0] = default;
        screen.GetViewportRow(4).Cells[7] = cell;
        SelectSecondFrame(screen, store);
        Assert.Equal(0, before[0].ViewportColumn);
        Assert.Equal(7, screen.GetKittyPlacements()[0].ViewportColumn);
        Assert.Equal(4, screen.GetKittyPlacements()[0].ViewportRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiedStoresOwnScratchAndCanPublishToIndependentScreens(bool virtualPlacement)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, 3);
        ReadOnlySpan<TerminalKittyImagePlacement> original = screen.GetKittyPlacements();
        TerminalScreen copy = screen.CreateStateCopy();
        ManagedKittyGraphicsStore copiedStore = store.CreateStateCopy();
        SelectSecondFrame(copy, copiedStore);
        Assert.True(original.Overlaps(copy.GetKittyPlacements()));
        copiedStore.Clear(copy);
        copiedStore.Publish(copy, 8, 16);
        Assert.Empty(copy.GetKittyPlacements().ToArray());
        store.Publish(screen, 8, 16);
        Assert.True(original.Overlaps(screen.GetKittyPlacements()));
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? source));
        Assert.Equal(1, source!.RgbaPixels[0]);
        Assert.False(copy.TryGetKittyImageSource(1, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CellMetricsAndPlacementChangesReplaceGeometry(bool virtualPlacement)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, 1);
        ReadOnlySpan<TerminalKittyImagePlacement> original = screen.GetKittyPlacements();
        store.Publish(screen, 16, 32);
        Assert.False(original.Overlaps(screen.GetKittyPlacements()));
        Assert.Equal(8, original[0].WidthPx);
        Assert.Equal(16, screen.GetKittyPlacements()[0].WidthPx);
        ManagedKittyGraphicsStore.Placement placement = store.Placements.Single().Value;
        placement.Options = placement.Options with { Columns = 2 };
        store.Publish(screen, 16, 32);
        Assert.False(original.Overlaps(screen.GetKittyPlacements()));
        if (!virtualPlacement) Assert.Equal(32, screen.GetKittyPlacements()[0].WidthPx);
    }

    private static void SelectSecondFrame(TerminalScreen screen, ManagedKittyGraphicsStore store)
    {
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        Assert.True(image.Animation.ApplyControl(Command("a=a,c=2")));
        store.MarkContentChanged(image);
        store.Publish(screen, 8, 16);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativePlacementsRetainPixelsOnlyGeometryButRebuildChangedOffsets(bool virtualPlacement)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, 1);
        Assert.True(store.TryAddImage(screen, 2, 0, new(new(1, 1, [4, 5, 6, 255])), false, out _));
        ManagedKittyGraphicsStore.Image child = store.Find(2)!;
        Assert.True(store.TryAddPlacement(screen, child, Command("a=p,i=2,p=1,P=1,Q=1,H=2,V=3,c=1,r=1"),
            0, 0, 8, 16, out _, out _));
        store.Publish(screen, 8, 16);
        ReadOnlySpan<TerminalKittyImagePlacement> previous = screen.GetKittyPlacements();
        SelectSecondFrame(screen, store);
        Assert.True(previous.Overlaps(screen.GetKittyPlacements()));
        Assert.Equal(2, previous.ToArray().Single(p => p.ImageId == 2).ViewportColumn);
        Assert.True(store.TryAddPlacement(screen, child, Command("a=p,i=2,p=1,P=1,Q=1,H=5,V=6,c=1,r=1"),
            0, 0, 8, 16, out _, out _));
        store.Publish(screen, 8, 16);
        TerminalKittyImagePlacement moved = screen.GetKittyPlacements().ToArray().Single(p => p.ImageId == 2);
        Assert.Equal(5, moved.ViewportColumn);
        Assert.Equal(6, moved.ViewportRow);
        Assert.Equal(2, previous.ToArray().Single(p => p.ImageId == 2).ViewportColumn);
    }

    [Fact]
    public void SameVirtualTargetWithDifferentSourceDimensionsInvalidatesProjection()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(true, 1);
        ReadOnlySpan<TerminalKittyImagePlacement> before = screen.GetKittyPlacements();
        Assert.True(store.TryAddImage(screen, 1, 0, new(new(2, 1, [8, 7, 6, 255, 5, 4, 3, 255])), false, out _));
        Assert.True(store.TryAddPlacement(screen, store.Find(1)!, Command("a=p,i=1,p=1,U=1,c=1,r=1"),
            0, 0, 8, 16, out _, out _));
        store.Publish(screen, 8, 16);
        Assert.Equal(1, before[0].SourceWidth);
        Assert.Equal(2, screen.GetKittyPlacements()[0].SourceWidth);
        Assert.False(before.Overlaps(screen.GetKittyPlacements()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameIdSameSizeReplacementPublishesNewPixels(bool virtualPlacement)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, 1);
        ReadOnlySpan<TerminalKittyImagePlacement> before = screen.GetKittyPlacements();
        TerminalScreen held = screen.CreateStateCopy();
        Assert.True(store.TryAddImage(screen, 1, 0, new(new(1, 1, [8, 7, 6, 255])), false, out _));
        Assert.True(store.TryAddPlacement(screen, store.Find(1)!,
            Command($"a=p,i=1,p=1,U={(virtualPlacement ? 1 : 0)},c=1,r=1"), 0, 0, 8, 16, out _, out _));
        store.Publish(screen, 8, 16);
        Assert.Equal(virtualPlacement, before.Overlaps(screen.GetKittyPlacements()));
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? current));
        Assert.Equal(8, current!.RgbaPixels[0]);
        Assert.True(held.TryGetKittyImageSource(1, out TerminalKittyImageSource? original));
        Assert.Equal(1, original!.RgbaPixels[0]);
    }

    [Fact]
    public void VirtualToPinnedReplacementDiscardsTheOldPlaceholderScene()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(true, 1);
        _ = screen.GetKittyPlacements();
        Assert.True(store.TryAddPlacement(screen, store.Find(1)!, Command("a=p,i=1,p=1,c=1,r=1"),
            4, 7, 8, 16, out _, out _));
        store.Publish(screen, 8, 16);
        Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(7, screen.GetKittyPlacements()[0].ViewportColumn);
        Assert.Equal(4, screen.GetKittyPlacements()[0].ViewportRow);
        screen.ClearKittyGraphics();
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        store.Publish(screen, 8, 16);
        Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(7, screen.GetKittyPlacements()[0].ViewportColumn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedScratchDoesNotRootRemovedImageSources(bool virtualPlacement)
    {
        (ManagedKittyGraphicsStore store, WeakReference<TerminalKittyImageSource> removed) = RemovePublishedImage(virtualPlacement);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.False(removed.TryGetTarget(out _));
        GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ManagedKittyGraphicsStore, WeakReference<TerminalKittyImageSource>) RemovePublishedImage(bool virtualPlacement)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(virtualPlacement, 64);
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? source));
        WeakReference<TerminalKittyImageSource> removed = new(source!);
        Assert.True(store.RemoveImage(screen, 1));
        screen.ClearKittyGraphics();
        // Do not publish again or reset the store: publication scratch must
        // have released the source immediately after the original publication.
        return (store, removed);
    }

    private static (TerminalScreen Screen, ManagedKittyGraphicsStore Store) Scene(bool virtualPlacement, int count)
    {
        TerminalScreen screen = new(80, 24, 100);
        ManagedKittyGraphicsStore store = new(4096);
        Assert.True(store.TryAddImage(screen, 1, 0, new(new(1, 1, [1, 2, 3, 255])), false, out _));
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        Assert.True(image.Animation.TryTransmitFrame(Command("a=f"), new(1, 1, [9, 8, 7, 255]), 4096, out _, out _));
        store.CommitAnimationBytes(image);
        for (int i = 0; i < count; i++)
        {
            Assert.True(store.TryAddPlacement(screen, image,
                Command($"a=p,i=1,p={i + 1},c=1,r=1,U={(virtualPlacement ? 1 : 0)}"),
                i / 40, i % 40, 8, 16, out _, out _));
            if (virtualPlacement)
                screen.GetViewportRow(i / 40).Cells[i % 40] = new()
                {
                    Codepoint = TerminalKittyPlaceholderScanner.Placeholder,
                    Grapheme = "\U0010EEEE\u0305\u0305",
                    ForegroundIdentity = TerminalColorIdentity.Rgb(1),
                    UnderlineIdentity = TerminalColorIdentity.Rgb((uint)i + 1),
                    Width = 1,
                };
        }
        store.Publish(screen, 8, 16);
        return (screen, store);
    }

    private static ManagedKittyGraphicsCommand Command(string text)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 4096, out var command));
        return command;
    }
}
