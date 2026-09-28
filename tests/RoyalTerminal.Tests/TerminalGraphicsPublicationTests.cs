// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty reserves image map capacity before eviction (graphics_storage.zig).
// WT ImageSlice copies resized pixels before installing storage; xterm's image
// addon uses JS canvas/Map lifetimes, not a CLR scene transaction. Preserve the
// complete Royal scene on preparation failure; retain COW readers independently.
public sealed class TerminalGraphicsPublicationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, (int)SnapshotMutationCheckpoint.KittyPlacementsPrepared)]
    [InlineData(false, (int)SnapshotMutationCheckpoint.KittyScenePrepared)]
    [InlineData(false, (int)SnapshotMutationCheckpoint.KittyImagesPrepared)]
    [InlineData(true, (int)SnapshotMutationCheckpoint.KittyPlacementsPrepared)]
    [InlineData(true, (int)SnapshotMutationCheckpoint.KittyScenePrepared)]
    [InlineData(true, (int)SnapshotMutationCheckpoint.KittyImagesPrepared)]
    public void PreparedSceneFailurePreservesImagesGeometryPlaceholdersAndCow(bool anchored, int checkpoint)
    {
        TerminalScreen screen = new(8, 2);
        TerminalScreenAnchor anchor = screen.CreateAnchor(0, 0);
        TerminalKittyImageSource old = Image(1), replacement = Image(2);
        Publish(screen, anchored, anchor, old);
        TerminalKittyImagePlacement[] placements = screen.GetKittyPlacements().ToArray();
        TerminalScreen retained = screen.CreateStateCopy();
        screen.GetViewportRow(0).IsDirty = false;
        OutOfMemoryException failure = new("Prepared scene failure");
        screen.MutationCheckpoint = phase => { if ((int)phase == checkpoint) throw failure; };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Publish(screen, anchored, anchor, replacement)));

        AssertImage(screen, old);
        Assert.False(screen.TryGetKittyImageSource(2, out _));
        Assert.True(screen.MatchesKittyPlaceholderScene(Targets(1), [], 10, 10));
        Assert.Equal(placements, screen.GetKittyPlacements().ToArray());
        Assert.False(screen.GetViewportRow(0).IsDirty);
        Assert.False(screen.SnapshotMutationFailed);
        screen.MutationCheckpoint = null;
        Publish(screen, anchored, anchor, replacement);
        AssertImage(screen, replacement);
        Assert.False(screen.TryGetKittyImageSource(1, out _));
        Assert.True(screen.MatchesKittyPlaceholderScene(Targets(2), [], 10, 10));
        AssertImage(retained, old);
        Assert.Equal(placements, retained.GetKittyPlacements().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingInputListDoesNotPartiallyPublishOrContaminateRetry(bool images)
    {
        TerminalScreen screen = new(8, 2);
        TerminalKittyImageSource old = Image(1), next = Image(2);
        screen.ReplaceKittyGraphics([old], [Placement(1)]);
        InvalidOperationException failure = new("Input failed midway");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => screen.ReplaceKittyGraphics(
            images ? new FailingList<TerminalKittyImageSource>(next, failure) : [next],
            images ? [Placement(2)] : new FailingList<TerminalKittyImagePlacement>(Placement(2), failure))));
        AssertImage(screen, old);
        Assert.Equal(1, screen.GetKittyPlacements()[0].ImageId);
        TerminalKittyImageSource final = Image(3);
        screen.ReplaceKittyGraphics([final], [Placement(3)]);
        AssertImage(screen, final);
        Assert.False(screen.TryGetKittyImageSource(2, out _));
    }

    [Fact]
    public void PixelOnlyPublicationReusesScratchAndRetainsProjection()
    {
        TerminalScreen screen = new(8, 2);
        TerminalScreenAnchor anchor = screen.CreateAnchor(0, 0);
        TerminalKittyImageSource[] red = [Image(1)], blue = [Image(1, 255)];
        TerminalKittyAnchoredPlacement[] placements = [new(anchor, 0, 0, 1, 1, Placement(1))];
        for (int i = 0; i < 4; i++) screen.ReplaceAnchoredKittyGraphics(red, placements, [], [], 10, 10);
        TerminalKittyImagePlacement projected = screen.GetKittyPlacements()[0];
        TerminalScreen retained = screen.CreateStateCopy();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            screen.ReplaceAnchoredKittyGraphics((i & 1) == 0 ? red : blue, placements, [], [], 10, 10);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(projected, screen.GetKittyPlacements()[0]);
        AssertImage(screen, blue[0]);
        AssertImage(retained, red[0]);
        // Adopted live dictionaries must never become another screen's scratch.
        TerminalScreen destination = new(8, 2);
        destination.AdoptStateFrom(screen);
        destination.ReplaceAnchoredKittyGraphics(red, placements, [], [], 10, 10);
        AssertImage(destination, red[0]);
        AssertImage(retained, red[0]);
    }

    [Fact]
    public void FailedPixelOnlyUpdateLeavesCachedGeometryAndAllOldPixels()
    {
        TerminalScreen screen = new(8, 2);
        TerminalScreenAnchor anchor = screen.CreateAnchor(0, 0);
        TerminalKittyImageSource old = Image(1), next = Image(1, 255);
        TerminalKittyAnchoredPlacement[] placements = [new(anchor, 0, 0, 1, 1, Placement(1))];
        screen.ReplaceAnchoredKittyGraphics([old], placements, [], [], 10, 10);
        TerminalKittyImagePlacement projected = screen.GetKittyPlacements()[0];
        screen.MutationCheckpoint = stage =>
        {
            if (stage == SnapshotMutationCheckpoint.KittyImagesPrepared) throw new OutOfMemoryException();
        };
        Assert.Throws<OutOfMemoryException>(() => screen.ReplaceAnchoredKittyGraphics([next], placements, [], [], 10, 10));
        AssertImage(screen, old);
        Assert.Same(projected, screen.GetKittyPlacements()[0]);
        screen.MutationCheckpoint = null;
        screen.ReplaceAnchoredKittyGraphics([next], placements, [], [], 10, 10);
        AssertImage(screen, next);
        Assert.Same(projected, screen.GetKittyPlacements()[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RasterReserveFailurePreservesStateAndSuccessfulRetryKeepsBufferOwnership(bool alternate)
    {
        TerminalScreen source = new(8, 2), destination = new(8, 2);
        AddRaster(destination, 1);
        if (alternate) destination.SwitchToAlternateBuffer(true);
        AddRaster(destination, 2);
        AddRaster(source, 3);
        destination.GetViewportRow(0)[0].Codepoint = 'X';
        TerminalScreen retained = destination.CreateStateCopy();
        OutOfMemoryException failure = new("Raster preparation failure");
        destination.MutationCheckpoint = stage =>
        {
            if (stage == SnapshotMutationCheckpoint.RasterPublicationPrepared) throw failure;
        };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => destination.ReplaceRasterGraphicsFrom(source)));
        Assert.True(destination.TryGetRasterImageSource(2, out _));
        Assert.False(destination.TryGetRasterImageSource(3, out _));
        Assert.Equal('X', destination.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.False(destination.SnapshotMutationFailed);
        destination.MutationCheckpoint = null;
        destination.ReplaceRasterGraphicsFrom(source);
        Assert.True(destination.TryGetRasterImageSource(3, out _));
        Assert.False(destination.TryGetRasterImageSource(2, out _));
        Assert.False(destination.GetViewportRow(0).ReadOnlyCells[0].HasContent);
        Assert.True(retained.TryGetRasterImageSource(2, out _));
        Assert.Equal('X', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        if (alternate)
        {
            destination.SwitchToPrimaryBuffer();
            Assert.True(destination.TryGetRasterImageSource(1, out _));
            destination.SwitchToAlternateBuffer(false);
            Assert.True(destination.TryGetRasterImageSource(3, out _));
        }
    }

    [Fact]
    public void RasterSelfReplacementPreservesImagesAndDoesNotEraseMoreText()
    {
        TerminalScreen screen = new(8, 2);
        AddRaster(screen, 1);
        TerminalRasterImagePlacement placement = screen.GetRasterImagePlacements()[0];
        screen.GetViewportRow(0)[0].Codepoint = 'X';
        screen.ReplaceRasterGraphicsFrom(screen);
        Assert.Same(placement, screen.GetRasterImagePlacements()[0]);
        Assert.True(screen.TryGetRasterImageSource(1, out _));
        Assert.Equal('X', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void RasterTextMutationFailureFaultsOnlyDestinationOwner()
    {
        TerminalScreen source = new(8, 2), destination = new(8, 2);
        AddRaster(source, 1);
        destination.SnapshotScrollbackQuota = new GhosttySnapshotScrollbackQuota { MaximumRows = 100 };
        using BasicVtProcessor processor = new(destination);
        processor.Process("text"u8);
        TerminalScreen retained = destination.CreateStateCopy();
        OutOfMemoryException failure = new("Covered row mutation");
        destination.MutationCheckpoint = stage =>
        {
            if (stage == SnapshotMutationCheckpoint.MetadataClear) throw failure;
        };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => destination.ReplaceRasterGraphicsFrom(source)));
        Assert.True(destination.SnapshotMutationFailed);
        Assert.False(source.SnapshotMutationFailed);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal('t', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedImagePublicationRetriesOnSubsequentNonGraphicsInput(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) { output.WriteLine("Native image publication comparison unavailable."); return; }
        TerminalScreen screen = new(8, 2);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.NotifyResize(8, 2, 80, 20);
        Write(processor, "\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\");
        Write(processor, "\u001b_Ga=f,f=32,i=1,s=1,v=1;AAD//w==\u001b\\");
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? old));
        screen.MutationCheckpoint = stage =>
        {
            if (stage == SnapshotMutationCheckpoint.KittyImagesPrepared) throw new OutOfMemoryException();
        };
        Assert.Throws<OutOfMemoryException>(() => Write(processor, "\u001b_Ga=a,i=1,c=2\u001b\\"));
        AssertImage(screen, old!);
        screen.MutationCheckpoint = null;
        Write(processor, "\u001b[5n");
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? updated));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, updated!.RgbaPixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, old!.RgbaPixels);
    }

    [Fact]
    public void ProjectionFailureDoesNotCacheNewRunsWithOldGeometry()
    {
        TerminalScreen screen = new(8, 2);
        TerminalCell placeholder = new()
        {
            Codepoint = 0x10EEEE, Width = 1, Grapheme = "\U0010EEEE\u0305\u0305",
            ForegroundIdentity = TerminalColorIdentity.Palette(1),
        };
        screen.GetViewportRow(0)[0] = placeholder;
        screen.ReplaceKittyGraphicsWithPlaceholders([Image(1)], [], Targets(1), [], 10, 10);
        Assert.Equal(0, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportColumn);
        TerminalScreen retained = screen.CreateStateCopy();
        screen.GetViewportRow(0)[0] = TerminalCell.Empty();
        screen.GetViewportRow(0)[2] = placeholder;
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.KittyProjectionPrepared) throw new OutOfMemoryException();
        };
        Assert.Throws<OutOfMemoryException>(() => screen.GetKittyPlacements().ToArray());
        // Without atomic run/projection publication the second call returned
        // stale column zero, having already cached the changed cell runs.
        Assert.Throws<OutOfMemoryException>(() => screen.GetKittyPlacements().ToArray());
        screen.MutationCheckpoint = null;
        Assert.Equal(2, Assert.Single(screen.GetKittyPlacements().ToArray()).ViewportColumn);
        Assert.Equal(0, Assert.Single(retained.GetKittyPlacements().ToArray()).ViewportColumn);
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingManagedPublicationRetriesAtTimerOrHoldRelease(bool held)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        processor.ResizeScreen(8, 2, 80, 20);
        Write(processor, "\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\");
        Write(processor, "\u001b_Ga=f,f=32,i=1,s=1,v=1;AAD//w==\u001b\\");
        bool fail = true;
        screen.MutationCheckpoint = phase =>
        {
            if (fail && phase == SnapshotMutationCheckpoint.KittyImagesPrepared) throw new OutOfMemoryException();
        };
        if (held) Write(processor, "\u001b[?2026h");
        Assert.Throws<OutOfMemoryException>(() => Write(processor, "\u001b_Ga=a,i=1,c=2\u001b\\"));
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? old));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, old!.RgbaPixels);
        fail = false;
        if (held) Write(processor, "\u001b[?2026l");
        else
        {
            Assert.Equal(TimeSpan.Zero, processor.NextTimedRefreshDelay);
            Assert.True(processor.RefreshTimedState());
        }
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? updated));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, updated!.RgbaPixels);
    }

    [Fact]
    public void ResizeRollbackRestoresPendingPublicationRetry()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        processor.ResizeScreen(8, 2, 80, 20);
        Write(processor, "\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\");
        Write(processor, "\u001b_Ga=f,f=32,i=1,s=1,v=1;AAD//w==\u001b\\");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.KittyImagesPrepared) throw new OutOfMemoryException();
        };
        Assert.Throws<OutOfMemoryException>(() => Write(processor, "\u001b_Ga=a,i=1,c=2\u001b\\"));
        screen.MutationCheckpoint = null;
        processor.ResizeCheckpoint = phase =>
        {
            if (phase == ManagedResizeCheckpoint.Ready) throw new OutOfMemoryException();
        };
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(9, 2, 90, 20));
        Assert.Equal(8, screen.Columns);
        Assert.Equal(TimeSpan.Zero, processor.NextTimedRefreshDelay);
        processor.ResizeCheckpoint = null;
        Write(processor, "\u001b[5n");
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? updated));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, updated!.RgbaPixels);
    }

    private static void Publish(TerminalScreen screen, bool anchored, TerminalScreenAnchor anchor, TerminalKittyImageSource image)
    {
        if (anchored) screen.ReplaceAnchoredKittyGraphics([image], [new(anchor, 0, 0, 1, 1, Placement(image.ImageId))],
            Targets(image.ImageId), [], 10, 10);
        else screen.ReplaceKittyGraphicsWithPlaceholders([image], [Placement(image.ImageId)], Targets(image.ImageId), [], 10, 10);
    }

    private static TerminalKittyImageSource Image(int id, byte blue = 0) => new(id, 1, 1, [255, 0, blue, 255]);
    private static TerminalKittyImagePlacement Placement(int id) => new(id, TerminalKittyImageLayer.AboveText,
        0, 0, 0, 0, 10, 10, 0, 0, 1, 1, 10, 10);
    private static TerminalKittyPlaceholderTarget[] Targets(int id) => [new(new((uint)id, 1, false), true, 1, 1)];
    private static void AssertImage(TerminalScreen screen, TerminalKittyImageSource expected)
    {
        Assert.True(screen.TryGetKittyImageSource(expected.ImageId, out TerminalKittyImageSource? actual));
        Assert.Same(expected, actual);
    }
    private static void AddRaster(TerminalScreen screen, int id) => screen.ReplaceRasterImage(
        new(id, TerminalRasterImageProtocol.Sixel, 1, 1, [255, 0, 0, 255]),
        new(id, TerminalRasterImageLayer.BelowText, 0, 0, 0, 0, 10, 10, 0, 0, 1, 1, 10, 10));
    private static void Write(IVtProcessor processor, string command) => processor.Process(Encoding.ASCII.GetBytes(command));

    private sealed class FailingList<T>(T first, Exception failure) : IReadOnlyList<T>
    {
        public int Count => 2;
        public T this[int index] => index == 0 ? first : throw failure;
        public IEnumerator<T> GetEnumerator() { yield return first; throw failure; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
