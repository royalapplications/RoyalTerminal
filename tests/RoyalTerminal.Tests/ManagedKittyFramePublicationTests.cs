// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyFramePublicationTests(ITestOutputHelper output)
{
    // Ghostty renderer/image.zig uses image generations and WT ImageSlice uses
    // revisions to avoid inspecting unchanged pixels. xterm.js TextureAtlas
    // similarly caches rasterized content. Our renderer uses content fingerprints:
    // keep those fingerprints/sources with immutable frame pixels, not the current
    // playback index. This preserves content deduplication without repeated hashing.
    [Theory]
    [InlineData(1)]
    [InlineData(512)]
    public void ReturningToImmutableFramesDoesNotRehashOrAllocate(int size)
    {
        ManagedKittyGraphicsStore.Image image = TwoFrames(size);
        ManagedKittyGraphicsCommand first = Command("a=a,c=1"), second = Command("a=a,c=2");
        TerminalKittyImageSource? last = null;
        for (int i = 0; i < 16; i++) Select(i);
        const int iterations = 256;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) Select(i);
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"size={size}, frameChanges={iterations}, bytes={bytes}, ms={milliseconds:F3}");
        GC.KeepAlive(last);
        Assert.Equal(0, bytes);

        void Select(int index)
        {
            image.Animation.ApplyControl((index & 1) == 0 ? first : second);
            last = image.Source;
        }
    }

    [Fact]
    public void FrameSourcesAreReusedAcrossPlaybackAndStateCopies()
    {
        ManagedKittyGraphicsStore.Image image = TwoFrames(1);
        TerminalKittyImageSource first = image.Source;
        ulong generation = image.Generation;
        long quota = image.QuotaBytes;
        image.Animation.ApplyControl(Command("a=a,c=2"));
        TerminalKittyImageSource second = image.Source;
        Assert.NotSame(first, second);
        ManagedKittyGraphicsStore.Image copy = image.CreateStateCopy();
        Assert.Same(second, copy.Source);
        image.Animation.ApplyControl(Command("a=a,c=1"));
        Assert.Same(first, image.Source);
        copy.Animation.ApplyControl(Command("a=a,c=1"));
        Assert.Same(first, copy.Source);
        Assert.Equal(generation, image.Generation);
        Assert.Equal(quota, image.QuotaBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrameEditsAndCompositionCreateFreshSourcesWithoutChangingReaders(bool compose)
    {
        ManagedKittyGraphicsStore.Image image = TwoFrames(1);
        TerminalKittyImageSource first = image.Source;
        ManagedKittyGraphicsStore.Image held = image.CreateStateCopy();
        if (compose)
            Assert.True(image.Animation.TryCompose(Command("a=c,r=2,c=1,w=1,h=1,C=2"), out _));
        else
            Assert.True(image.Animation.TryTransmitFrame(Command("a=f,r=1,X=1"), new(1, 1, [9, 8, 7, 255]),
                0, out _, out _));
        TerminalKittyImageSource changed = image.Source;
        Assert.NotSame(first, changed);
        Assert.NotEqual(first.ContentFingerprint, changed.ContentFingerprint);
        Assert.Equal(TerminalImageContentHash.HashBytes(changed.RgbaPixels), changed.ContentFingerprint);
        Assert.Same(first, held.Source);
        Assert.Equal(1, first.RgbaPixels[0]);
        image.Animation.ApplyControl(Command("a=a,c=2"));
        _ = image.Source;
        image.Animation.ApplyControl(Command("a=a,c=1"));
        Assert.Same(changed, image.Source);
    }

    [Fact]
    public void FrameDeletionAndImageIdReuseNeverResurrectAnOldSource()
    {
        ManagedKittyGraphicsStore.Image image = TwoFrames(1);
        TerminalKittyImageSource first = image.Source;
        image.Animation.ApplyControl(Command("a=a,c=2"));
        TerminalKittyImageSource second = image.Source;
        Assert.True(image.Animation.DeleteFrame(1, out _));
        Assert.Same(second, image.Source);
        Assert.True(image.Animation.TryTransmitFrame(Command("a=f"), new(1, 1, [9, 8, 7, 255]),
            100, out _, out _));
        image.Animation.ApplyControl(Command("a=a,c=2"));
        Assert.NotSame(first, image.Source);
        Assert.NotSame(second, image.Source);
        Assert.Equal(9, image.Source.RgbaPixels[0]);

        ManagedKittyGraphicsStore.Image replacement = TwoFrames(1);
        Assert.Equal(image.Id, replacement.Id);
        Assert.NotSame(first, replacement.Source);
        Assert.Equal(1, replacement.Source.RgbaPixels[0]);
    }

    private static ManagedKittyGraphicsStore.Image TwoFrames(int size)
    {
        byte[] first = new byte[size * size * 4], second = new byte[first.Length];
        first.AsSpan().Fill(1);
        second.AsSpan().Fill(2);
        ManagedKittyGraphicsStore.Image image = new(1, 0, new(new(size, size, first)), first.Length, false, 7);
        Assert.True(image.Animation.TryTransmitFrame(Command("a=f,X=1"), new(size, size, second),
            first.Length * 2L, out _, out _));
        return image;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelSourcesRespectIdsAndPreserveRgbQuotaDuringPublication(bool rgb)
    {
        ManagedKittyImagePixels pixels = rgb
            ? ManagedKittyImagePixels.FromRgb(1, 1, [1, 2, 3])
            : new(new(1, 1, [1, 2, 3, 255]));
        TerminalKittyImageSource source = pixels.GetSource(1);
        Assert.Same(source, pixels.GetSource(1));
        Assert.Equal(rgb ? 3 : 4, pixels.StorageBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, source.RgbaPixels);
        Assert.Equal(TerminalImageContentHash.HashBytes(source.RgbaPixels), source.ContentFingerprint);
        Assert.Throws<ArgumentOutOfRangeException>(() => pixels.GetSource(0));
        Assert.Same(source, pixels.GetSource(1));
        ManagedKittyImagePixels promoted = pixels.AsRgba();
        Assert.True(promoted.IsRgba);
        Assert.Equal(4, promoted.StorageBytes);
        Assert.Same(source, promoted.GetSource(1));
        Assert.Same(source.RgbaPixels, promoted.GetRgbaImage().Rgba);
        TerminalKittyImageSource differentId = promoted.GetSource(-1);
        Assert.Equal(-1, differentId.ImageId); // uint.MaxValue protocol identity.
        Assert.NotSame(source, differentId);
        Assert.Same(source.RgbaPixels, differentId.RgbaPixels);
        Assert.Equal(source.ContentFingerprint, differentId.ContentFingerprint);
        Assert.Equal(1, promoted.GetSource(1).ImageId);
        Assert.Equal(-1, differentId.ImageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedFrameCachesLiveOnlyAsLongAsTheirPixelOwners(bool holdCopy)
    {
        (ManagedKittyGraphicsStore.Image owner, WeakReference<TerminalKittyImageSource> removed) = DeleteFirstFrame(holdCopy);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.Equal(holdCopy, removed.TryGetTarget(out _));
        GC.KeepAlive(owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ManagedKittyGraphicsStore.Image, WeakReference<TerminalKittyImageSource>) DeleteFirstFrame(bool holdCopy)
    {
        ManagedKittyGraphicsStore.Image image = TwoFrames(1);
        WeakReference<TerminalKittyImageSource> source = new(image.Source);
        ManagedKittyGraphicsStore.Image owner = holdCopy ? image.CreateStateCopy() : image;
        image.Animation.ApplyControl(Command("a=a,c=2"));
        _ = image.Source;
        Assert.True(image.Animation.DeleteFrame(1, out _));
        return (owner, source);
    }

    private static ManagedKittyGraphicsCommand Command(string text)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 4096, out var command));
        return command;
    }
}
