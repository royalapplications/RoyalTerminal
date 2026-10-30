// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty eea7beaf7: snapshot payloads reserve full storage and use a stable
// transmission ticket independently of animation content revisions. The closest
// xterm.js ImageStorage and Windows Terminal ImageSlice models do not expose
// this snapshot/animation contract, so Ghostty is the interoperability reference.
public sealed class ManagedKittyPendingImageTests
{
    [Theory]
    [InlineData((byte)0, new byte[] { 12, 34, 56 }, new byte[] { 12, 34, 56, 255 })]
    [InlineData((byte)1, new byte[] { 12, 34, 56, 78 }, new byte[] { 12, 34, 56, 78 })]
    [InlineData((byte)3, new byte[] { 12, 34 }, new byte[] { 12, 12, 12, 34 })]
    [InlineData((byte)4, new byte[] { 12 }, new byte[] { 12, 12, 12, 255 })]
    public void PendingPixelsReserveProtocolFormatAndCompleteWithoutChangingRetainedOwner(byte format, byte[] pixels, byte[] rgba)
    {
        ManagedKittyImagePixels pending = ManagedKittyImagePixels.Pending(1, 1, format, pixels.Length);
        Assert.True(pending.IsPending);
        Assert.Empty(pending.Data.ToArray());
        Assert.Equal(pixels.Length, pending.StorageBytes);
        Assert.Equal(format, pending.Format);
        Assert.Null(pending.Complete([]));
        Assert.Throws<InvalidOperationException>(() => pending.GetRgbaImage());
        ManagedKittyImagePixels complete = Assert.IsType<ManagedKittyImagePixels>(pending.Complete(pixels));
        Assert.True(pending.IsPending);
        Assert.False(complete.IsPending);
        Assert.Equal(pixels, complete.Data.ToArray());
        Assert.Equal(format, complete.Format);
        Assert.Equal(rgba, complete.GetRgbaImage().Rgba);
        Assert.Equal(pixels.Length, complete.StorageBytes);
        Assert.Equal(rgba, complete.AsRgba().Data.ToArray());
        Assert.Null(complete.Complete(pixels));
    }

    [Theory]
    [InlineData(0, 1, 1, 4)]
    [InlineData(1, 0, 1, 4)]
    [InlineData(1, 1, 2, 4)]
    [InlineData(1, 1, 1, 3)]
    [InlineData(int.MaxValue, int.MaxValue, 1, 4)]
    public void InvalidPendingMetadataDoesNotAllocatePixels(int width, int height, byte format, int bytes)
        => Assert.ThrowsAny<ArgumentException>(() => ManagedKittyImagePixels.Pending(width, height, format, bytes));

    [Fact]
    public void StableTicketSurvivesContentChangesAndCompletesOnlyOnce()
    {
        TerminalScreen screen = new(3, 3);
        ManagedKittyGraphicsStore store = new(12);
        Assert.True(store.TryAddPendingImage(screen, 1, 0, Pending(), false, out var ticket, out _));
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        Assert.Equal(ticket.Transmission, image.Generation);
        store.MarkContentChanged(image);
        ulong generation = image.Generation;
        ulong revision = store.Revision;
        Assert.NotEqual(ticket.Transmission, generation);
        Assert.False(ticket.Complete(store, [1, 2]));
        Assert.True(ticket.Complete(store, [1, 2, 3, 4]));
        Assert.Equal(generation, image.Generation);
        Assert.Equal(ticket.Transmission, image.Transmission);
        Assert.True(store.Revision > revision);
        Assert.Equal(4, store.StoredBytes);
        Assert.False(ticket.Complete(store, [4, 3, 2, 1]));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.Animation.RootPixels.Data.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleTicketsCannotCompleteReplacementOrEvictedImages(bool evict)
    {
        TerminalScreen screen = new(3, 3);
        ManagedKittyGraphicsStore store = new(4);
        Assert.True(store.TryAddPendingImage(screen, 1, 0, Pending(), false, out var stale, out _));
        Assert.True(store.TryAddPendingImage(screen, evict ? 2u : 1u, 0, Pending(), false, out var current, out _));
        Assert.Equal(4, store.StoredBytes);
        Assert.False(stale.Complete(store, [1, 2, 3, 4]));
        Assert.True(current.Complete(store, [9, 8, 7, 6]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingFramesBlockPlaybackAndPublicationUntilTheirPayloadArrives(bool virtualPlacement)
    {
        TerminalScreen screen = new(3, 3);
        ManagedKittyGraphicsStore store = new(12);
        Assert.True(store.TryAddPendingImage(screen, 1, 0, Pending(), false, out var ticket, out _));
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        image.Animation = ManagedKittyAnimation.Restore(image.Animation.RootPixels,
            [Pending(), Pending()], [10, 20, 30], 2, 7, 2, 2);
        Assert.True(store.TryReserveAnimation(screen, image, 8));
        store.CommitAnimationBytes(image);
        Assert.Equal(12, store.StoredBytes);
        Assert.True(store.TryAddPlacement(screen, image, Command("a=p,i=1,p=1" + (virtualPlacement ? ",U=1" : "")),
            0, 0, 8, 16, out _, out _));
        store.Publish(screen, 8, 16);
        Assert.False(screen.TryGetKittyImageSource(1, out _));
        Assert.False(image.Animation.Tick(0, true, out long? delay));
        Assert.Null(delay);
        Assert.True(ticket.Complete(store, [1, 2, 3, 4]));
        Assert.False(ticket.CompleteFrame(store, 1, [1, 2, 3, 4]));
        Assert.False(ticket.CompleteFrame(store, 4, [1, 2, 3, 4]));
        Assert.False(ticket.CompleteFrame(store, 3, [1, 2, 3]));
        ulong generation = image.Generation;
        Assert.True(ticket.CompleteFrame(store, 3, [9, 8, 7, 6]));
        Assert.True(image.Generation > generation);
        Assert.Equal(ticket.Transmission, image.Transmission);
        store.Publish(screen, 8, 16);
        Assert.True(screen.TryGetKittyImageSource(1, out var source));
        Assert.NotNull(source);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, source.RgbaPixels);
        Assert.False(image.Animation.Tick(0, true, out delay));
        Assert.Null(delay); // The displayed frame is ready, but frame 2 is pending.
        generation = image.Generation;
        Assert.True(ticket.CompleteFrame(store, 2, [5, 5, 5, 5]));
        Assert.Equal(generation, image.Generation);
        Assert.False(image.Animation.HasPendingFrames);
        Assert.False(image.Animation.Tick(0, true, out delay));
        Assert.Equal(30, delay);
        Assert.True(image.Animation.Tick(30, true, out delay));
        Assert.Equal(1u, image.Animation.CurrentFrameNumber);
        Assert.Equal(3u, image.Animation.CurrentLoop);
        Assert.Equal(7u, image.Animation.MaximumLoops);
        Assert.Equal(2u, image.Animation.State);
        Assert.Equal(12, store.StoredBytes);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void DeletingPendingFramesReleasesReservationAndCanPromotePendingRoot(uint removed)
    {
        TerminalScreen screen = new(3, 3);
        ManagedKittyGraphicsStore store = new(12);
        Assert.True(store.TryAddPendingImage(screen, 1, 0, Pending(), false, out _, out _));
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        image.Animation = ManagedKittyAnimation.Restore(image.Animation.RootPixels,
            [Pending(), new(new(1, 1, [1, 2, 3, 4]))], [10, 20, 30], 2, 0, 0, 2);
        store.CommitAnimationBytes(image);
        Assert.True(image.Animation.DeleteFrame(removed, out bool changed));
        Assert.False(changed);
        store.CommitAnimationBytes(image);
        Assert.Equal(8, store.StoredBytes);
        Assert.True(image.Animation.RootPixels.IsPending);
        Assert.Equal(removed == 1 ? 20u : 10u, image.Animation.FrameGap(0));
        Assert.True(image.Animation.TryCompletePendingFrame(1, [4, 3, 2, 1]));
        Assert.False(image.Animation.HasPendingFrames);
    }

    [Theory]
    [InlineData("a=f,r=2,z=99,X=1", "EINVAL: frame data incomplete")]
    [InlineData("a=f,c=2", "EINVAL: base frame not found")]
    [InlineData("a=c,r=2,c=1", "ENOENT: source frame not found")]
    [InlineData("a=c,r=1,c=2", "ENOENT: destination frame not found")]
    public void ProtocolEditsRejectPendingFramesWithoutChangingGapsOrReservations(string input, string expected)
    {
        ManagedKittyAnimation animation = ManagedKittyAnimation.Restore(new(new(1, 1, [1, 2, 3, 4])),
            [Pending()], [10, 20], 2, 0, 0, 1);
        ManagedKittyGraphicsCommand command = Command(input);
        bool result = command.Action == 'f'
            ? animation.TryTransmitFrame(command, new(1, 1, [9, 8, 7, 6]), 100, out _, out string error)
            : animation.TryCompose(command, out error);
        Assert.False(result);
        Assert.Equal(expected, error);
        Assert.Equal(20u, animation.FrameGap(1));
        Assert.Equal(8, animation.StoredBytes);
        Assert.True(animation.CurrentPixels.IsPending);
    }

    [Fact]
    public void PendingRootRejectsCompositionAndFrameTransmission()
    {
        ManagedKittyAnimation animation = new(Pending());
        Assert.False(animation.TryTransmitFrame(Command("a=f"), new(1, 1, [1, 2, 3, 4]), 100, out _, out string error));
        Assert.Equal("EINVAL: image data incomplete", error);
        Assert.False(animation.TryCompose(Command("a=c,r=1,c=1"), out error));
        Assert.Equal("EINVAL: image data incomplete", error);
        Assert.True(animation.RootPixels.IsPending);
    }

    [Fact]
    public void SnapshotCopyOwnsCompletionAndRetainsStableTransmission()
    {
        TerminalScreen screen = new(3, 3);
        ManagedKittyGraphicsStore store = new(8);
        Assert.True(store.TryAddPendingImage(screen, 1, 2, Pending(), true, out var ticket, out _));
        ManagedKittyGraphicsStore.Image image = store.Find(1)!;
        image.ImplicitId = true;
        image.Animation = ManagedKittyAnimation.Restore(image.Animation.RootPixels, [Pending()], [10, 20], 1, 5, 2, 1);
        store.CommitAnimationBytes(image);
        ManagedKittyGraphicsStore copy = store.CreateStateCopy();
        ManagedKittyGraphicsStore.Image copied = copy.Find(1)!;
        Assert.Equal(image.Transmission, copied.Transmission);
        Assert.True(copied.Transient);
        Assert.True(copied.ImplicitId);
        Assert.True(ticket.Complete(copy, [1, 2, 3, 4]));
        Assert.True(ticket.CompleteFrame(copy, 2, [5, 6, 7, 8]));
        Assert.True(image.Animation.HasPendingFrames);
        Assert.False(copied.Animation.HasPendingFrames);
        Assert.Equal(1u, copied.Animation.State);
        Assert.Equal(1, copied.Animation.CurrentIndex);
        Assert.Equal(5u, copied.Animation.MaximumLoops);
        Assert.Equal(2u, copied.Animation.CurrentLoop);
    }

    [Fact]
    public void PendingReservationObeysQuotaAndAnimationRestoreRejectsInvalidShapes()
    {
        TerminalScreen screen = new(3, 3);
        ManagedKittyGraphicsStore store = new(3);
        Assert.False(store.TryAddPendingImage(screen, 1, 0, Pending(), false, out _, out _));
        Assert.Equal(0, store.StoredBytes);
        Assert.Equal(0, store.ImageCount);
        Assert.Throws<ArgumentException>(() => store.TryAddPendingImage(screen, 1, 0,
            new(new(1, 1, [1, 2, 3, 4])), false, out _, out _));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimation.Restore(Pending(), [], [10], 3, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimation.Restore(Pending(), [], [], 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimation.Restore(Pending(), [], [10], 0, 0, 0, 1));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimation.Restore(Pending(),
            [ManagedKittyImagePixels.Pending(1, 1, 0, 3)], [10, 20], 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => ManagedKittyAnimation.Restore(Pending(),
            [ManagedKittyImagePixels.Pending(2, 1, 1, 8)], [10, 20], 0, 0, 0, 0));
    }

    private static ManagedKittyImagePixels Pending() => ManagedKittyImagePixels.Pending(1, 1, 1, 4);
    private static ManagedKittyGraphicsCommand Command(string text)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 4096, out ManagedKittyGraphicsCommand? command));
        return command;
    }
}
