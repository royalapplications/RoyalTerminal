// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyAnimationTests
{
    [Fact]
    public void StateCopyOwnsFrameListAndClockButSharesImmutablePixels()
    {
        ManagedKittyAnimation animation = TwoFrames();
        animation.ApplyControl(Command("a=a,r=1,z=40,s=3"));
        Assert.False(animation.Tick(0, true, out _));
        ManagedKittyAnimation copy = animation.CreateStateCopy();
        Assert.Same(animation.CurrentPixels, copy.CurrentPixels);
        Assert.True(copy.Tick(40, true, out _));
        Assert.Equal(1u, animation.CurrentFrameNumber);
        Assert.Equal(2u, copy.CurrentFrameNumber);
        Assert.True(animation.Tick(40, true, out _));
        Assert.Same(animation.CurrentPixels, copy.CurrentPixels);
        ManagedKittyImagePixels retained = animation.CurrentPixels;
        Append(copy, "a=f,r=2,X=1", Pixel(1, 2, 3));
        Assert.Same(retained, animation.CurrentPixels);
        Assert.NotSame(retained, copy.CurrentPixels);
        Assert.True(copy.DeleteFrame(1, out _));
        Assert.Equal(2, animation.FrameCount);
        Assert.Equal(1, copy.FrameCount);
    }

    [Fact]
    public void AppendUsesBackgroundClipsAndReservesOneFullCanvas()
    {
        ManagedKittyAnimation animation = Create(new(2, 2, new byte[16]));
        ManagedKittyGraphicsCommand command = Command("a=f,x=1,y=1,Y=4278190335,X=1");
        Assert.Equal(16, animation.RequiredAdditionalBytes(command));
        Assert.True(animation.TryTransmitFrame(command, new(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]), 32, out uint number, out string error));
        Assert.Equal("OK", error);
        Assert.Equal(2u, number);
        Assert.Equal(32, animation.StoredBytes);
        Assert.Equal(1u, animation.CurrentFrameNumber);

        Assert.True(animation.ApplyControl(Command("a=a,c=2")));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 1, 2, 3, 4 }, animation.CurrentImage.Rgba);
    }

    [Fact]
    public void RejectedAppendIsAtomicAndFrameEditsDoNotRequireAdditionalQuota()
    {
        ManagedKittyAnimation animation = Create(Pixel(255, 0, 0));
        Assert.False(animation.TryTransmitFrame(Command("a=f"), Pixel(0, 0, 255), 4, out _, out string error));
        Assert.Equal("ENOSPC: animation frame storage full", error);
        Assert.Equal(1, animation.FrameCount);
        Assert.False(animation.TryTransmitFrame(Command("a=f,c=99"), Pixel(0, 0, 255), 100, out _, out error));
        Assert.Equal("EINVAL: base frame not found", error);
        Assert.False(animation.TryTransmitFrame(Command("a=f"), new(2, 1, new byte[8]), 100, out _, out error));
        Assert.Equal("EINVAL: frame dimensions exceed image", error);

        KittyGraphicsDecodedImage previous = animation.CurrentImage;
        ManagedKittyGraphicsCommand edit = Command("a=f,r=1,X=1");
        Assert.Equal(0, animation.RequiredAdditionalBytes(edit));
        Assert.True(animation.TryTransmitFrame(edit, Pixel(0, 0, 255), 0, out uint frame, out _));
        Assert.Equal(1u, frame);
        Assert.Equal(4, animation.StoredBytes);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, previous.Rgba);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, animation.CurrentImage.Rgba);
    }

    [Fact]
    public void ExistingFrameCanvasAndGapDefaultsFollowProtocol()
    {
        ManagedKittyAnimation animation = Create(new(2, 1, [255, 0, 0, 255, 0, 255, 0, 255]));
        Append(animation, "a=f,c=1,x=1,r=999", Pixel(0, 0, 255));
        animation.ApplyControl(Command("a=a,s=3"));
        Assert.True(animation.Tick(0, true, out long? delay));
        Assert.Equal(40, delay);
        Assert.Equal(2u, animation.CurrentFrameNumber);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, animation.CurrentImage.Rgba);
        Append(animation, "a=f,r=2,x=0,X=1,z=0", Pixel(1, 2, 3));
        Assert.False(animation.Tick(100, true, out delay));
        Assert.Equal(40, delay); // Editing displayed frame restarts its unchanged gap.
    }

    [Theory]
    [InlineData("a=c,r=0,c=1", "ENOENT: source frame not found")]
    [InlineData("a=c,r=1,c=0", "ENOENT: destination frame not found")]
    [InlineData("a=c,r=1,c=1,x=4294967295,w=2", "EINVAL: destination rectangle out of bounds")]
    [InlineData("a=c,r=1,c=1,X=4294967295,w=2", "EINVAL: source rectangle out of bounds")]
    [InlineData("a=c,r=1,c=1,w=1,h=1", "EINVAL: source and destination rectangles overlap")]
    public void CompositionRejectsInvalidRectanglesWithoutChangingPixels(string input, string expected)
    {
        ManagedKittyAnimation animation = Create(new(3, 1, new byte[12]));
        KittyGraphicsDecodedImage before = animation.CurrentImage;
        Assert.False(animation.TryCompose(Command(input), out string error));
        Assert.Equal(expected, error);
        Assert.Same(before, animation.CurrentImage);
    }

    [Fact]
    public void CompositionAllowsDisjointSelfRectanglesAndPreservesPublishedPixels()
    {
        ManagedKittyAnimation animation = Create(new(3, 1, [1, 2, 3, 4, 0, 0, 0, 0, 9, 9, 9, 9]));
        KittyGraphicsDecodedImage before = animation.CurrentImage;
        Assert.True(animation.TryCompose(Command("a=c,r=1,c=1,w=1,h=1,x=2,C=2"), out string error));
        Assert.Equal("OK", error);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0, 1, 2, 3, 4 }, animation.CurrentImage.Rgba);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0, 9, 9, 9, 9 }, before.Rgba);
    }

    [Theory]
    [InlineData(new byte[] { 10, 20, 30, 40 }, new byte[] { 100, 110, 120, 255 }, new byte[] { 100, 110, 120, 255 })]
    [InlineData(new byte[] { 10, 20, 30, 40 }, new byte[] { 100, 110, 120, 0 }, new byte[] { 10, 20, 30, 40 })]
    [InlineData(new byte[] { 0, 0, 0, 255 }, new byte[] { 255, 255, 255, 128 }, new byte[] { 128, 128, 128, 255 })]
    [InlineData(new byte[] { 0, 0, 0, 0 }, new byte[] { 200, 100, 50, 128 }, new byte[] { 200, 100, 50, 128 })]
    [InlineData(new byte[] { 255, 0, 0, 0 }, new byte[] { 0, 0, 255, 0 }, new byte[] { 0, 0, 255, 0 })]
    public void AlphaCompositionMatchesUpstreamWuffsEdgeCases(byte[] destination, byte[] source, byte[] expected)
    {
        ManagedKittyAnimation animation = Create(new(1, 1, destination));
        Append(animation, "a=f,r=1", new(1, 1, source));
        Assert.Equal(expected, animation.CurrentImage.Rgba);
    }

    [Fact]
    public void AlphaCompositionMatchesNativeForDeterministicTranslucentPixels()
    {
        if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        byte[] source = new byte[1024];
        byte[] destination = new byte[1024];
        Random random = new(17);
        random.NextBytes(source);
        random.NextBytes(destination);
        ManagedKittyAnimation animation = Create(new(256, 1, destination));
        Append(animation, "a=f,r=1", new(256, 1, source));
        using GhosttyTerminal native = new(10, 3);
        native.SetKittyImageStorageLimit(4096);
        native.Write(Encoding.ASCII.GetBytes($"\u001b_Ga=t,i=1,f=32,s=256,v=1;{Convert.ToBase64String(destination)}\u001b\\"));
        native.Write(Encoding.ASCII.GetBytes($"\u001b_Ga=f,i=1,r=1,f=32,s=256,v=1;{Convert.ToBase64String(source)}\u001b\\"));
        Assert.True(native.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage image));
        Assert.Equal(image.CopyRgbaData(), animation.CurrentImage.Rgba);
    }

    [Fact]
    public void GaplessFramesAreSkippedAndOnlyOneDisplayedFrameAdvancesPerTick()
    {
        ManagedKittyAnimation animation = Create(Pixel(255, 0, 0));
        Append(animation, "a=f,z=-1", Pixel(1, 1, 1));
        Append(animation, "a=f,z=25", Pixel(0, 0, 255));
        Append(animation, "a=f,z=30", Pixel(0, 255, 0));
        animation.ApplyControl(Command("a=a,s=3"));
        Assert.True(animation.Tick(0, true, out long? delay));
        Assert.Equal(3u, animation.CurrentFrameNumber);
        Assert.Equal(25, delay);
        Assert.True(animation.Tick(1000, true, out delay));
        Assert.Equal(4u, animation.CurrentFrameNumber);
        Assert.Equal(30, delay);
        Assert.True(animation.Tick(2000, true, out delay));
        Assert.Equal(3u, animation.CurrentFrameNumber);
        Assert.Equal(25, delay);
    }

    [Fact]
    public void LoadingAnimationParksUntilMoreFramesArrive()
    {
        ManagedKittyAnimation animation = TwoFrames();
        animation.ApplyControl(Command("a=a,s=2"));
        Assert.True(animation.Tick(0, true, out long? delay));
        Assert.Equal(40, delay);
        Assert.False(animation.Tick(100, true, out delay));
        Assert.Null(delay);
        Assert.Equal(2u, animation.CurrentFrameNumber);
        Append(animation, "a=f,z=25", Pixel(0, 255, 0));
        Assert.True(animation.Tick(150, true, out delay));
        Assert.Equal(3u, animation.CurrentFrameNumber);
        Assert.Equal(25, delay);
    }

    [Fact]
    public void FiniteLoopBudgetParksAndStateCommandRestartsIt()
    {
        ManagedKittyAnimation animation = TwoFrames();
        animation.ApplyControl(Command("a=a,r=1,z=10,s=3,v=2"));
        Assert.False(animation.Tick(0, true, out long? delay));
        Assert.Equal(10, delay);
        Assert.True(animation.Tick(10, true, out delay));
        Assert.Equal(40, delay);
        Assert.False(animation.Tick(50, true, out delay));
        Assert.Null(delay);
        Assert.False(animation.Tick(500, true, out delay));
        Assert.Equal(2u, animation.CurrentFrameNumber);
        animation.ApplyControl(Command("a=a,s=3,v=1"));
        Assert.True(animation.Tick(500, true, out delay));
        Assert.Equal(1u, animation.CurrentFrameNumber);
        Assert.Equal(10, delay);
    }

    [Fact]
    public void InvalidControlFieldsAreIgnoredIndependently()
    {
        ManagedKittyAnimation animation = TwoFrames();
        Assert.True(animation.ApplyControl(Command("a=a,r=99,z=12,c=2,s=99,v=0")));
        Assert.False(animation.Tick(0, true, out long? delay));
        Assert.Null(delay);
        Assert.False(animation.ApplyControl(Command("a=a,r=1,z=10,c=99,s=3")));
        Assert.False(animation.Tick(0, true, out delay));
        Assert.Equal(40, delay);
    }

    [Fact]
    public void StoppedUnplacedSingleFrameAndAllGaplessDoNotSchedule()
    {
        ManagedKittyAnimation animation = Create(Pixel(255, 0, 0));
        animation.ApplyControl(Command("a=a,s=3,r=1,z=10"));
        Assert.False(animation.Tick(0, true, out long? delay));
        Assert.Null(delay);
        Append(animation, "a=f,z=-1", Pixel(0, 0, 255));
        Assert.False(animation.Tick(0, false, out delay));
        Assert.Null(delay);
        animation.ApplyControl(Command("a=a,r=1,z=-1"));
        Assert.False(animation.Tick(0, true, out delay));
        Assert.Null(delay);
        animation.ApplyControl(Command("a=a,r=1,z=10,s=1"));
        Assert.False(animation.Tick(0, true, out delay));
        Assert.Null(delay);
    }

    [Fact]
    public void RestartedClockReanchorsAndTimestampAdditionSaturates()
    {
        ManagedKittyAnimation animation = TwoFrames();
        animation.ApplyControl(Command("a=a,c=2,s=3"));
        Assert.False(animation.Tick(1000, true, out _));
        Assert.False(animation.Tick(5, true, out long? delay));
        Assert.Equal(40, delay);
        Assert.True(animation.Tick(45, true, out delay));
        Assert.Equal(40, delay);
        animation.ApplyControl(Command("a=a,s=1"));
        animation.ApplyControl(Command("a=a,s=3"));
        Assert.False(animation.Tick(long.MaxValue - 1, true, out delay));
        Assert.Equal(1, delay);
        Assert.True(animation.Tick(long.MaxValue, true, out delay));
        Assert.Null(delay);
    }

    [Fact]
    public void RootDeletionPromotesPixelsAndGapAndCreditsStorage()
    {
        ManagedKittyAnimation animation = TwoFrames();
        Assert.True(animation.DeleteFrame(0, out bool visibleChanged));
        Assert.True(visibleChanged);
        Assert.Equal(4, animation.StoredBytes);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, animation.RootImage.Rgba);
        Assert.False(animation.DeleteFrame(999, out visibleChanged));
        Assert.False(visibleChanged);
        Append(animation, "a=f,z=10", Pixel(0, 255, 0));
        animation.ApplyControl(Command("a=a,s=3"));
        Assert.False(animation.Tick(0, true, out long? delay));
        Assert.Equal(40, delay);
    }

    [Theory]
    [InlineData(2u, 2u, 2u, true, 2)]
    [InlineData(3u, 2u, 2u, false, 2)]
    [InlineData(2u, 3u, 2u, false, 1)]
    [InlineData(4u, 999u, 3u, true, 2)]
    public void DeletionPreservesDisplayedFrameIdentityWhenPossible(uint current, uint removed, uint expectedCurrent, bool changed, int expectedColor)
    {
        ManagedKittyAnimation animation = TwoFrames();
        Append(animation, "a=f", Pixel(255, 255, 255));
        Append(animation, "a=f", Pixel(0, 255, 0));
        animation.ApplyControl(Command($"a=a,c={current}"));
        Assert.True(animation.DeleteFrame(removed, out bool visibleChanged));
        Assert.Equal(changed, visibleChanged);
        Assert.Equal(expectedCurrent, animation.CurrentFrameNumber);
        Assert.Equal(expectedColor == 1 ? new byte[] { 0, 0, 255, 255 } : new byte[] { 255, 255, 255, 255 }, animation.CurrentImage.Rgba);
    }

    [Fact]
    public void SteadyStateAnimationTicksDoNotAllocate()
    {
        ManagedKittyAnimation animation = TwoFrames();
        animation.ApplyControl(Command("a=a,s=3"));
        animation.Tick(0, true, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) animation.Tick(i, true, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void DeletingEarlierFramePreservesLastFrameIdentityAndElapsedGap(uint removed)
    {
        ManagedKittyAnimation animation = TwoFrames();
        Append(animation, "a=f,z=60", Pixel(0, 255, 0));
        animation.ApplyControl(Command("a=a,c=3,s=3"));
        Assert.False(animation.Tick(100, true, out long? delay));
        Assert.Equal(60, delay);
        ManagedKittyImagePixels displayed = animation.CurrentPixels;
        Assert.True(animation.DeleteFrame(removed, out bool changed));
        Assert.False(changed);
        Assert.Same(displayed, animation.CurrentPixels);
        Assert.False(animation.Tick(120, true, out delay));
        Assert.Equal(40, delay);
        Assert.True(animation.Tick(160, true, out delay));
        Assert.Equal(removed == 1 ? 40 : 60, delay);
    }

    private static ManagedKittyAnimation TwoFrames()
    {
        ManagedKittyAnimation animation = Create(Pixel(255, 0, 0));
        Append(animation, "a=f", Pixel(0, 0, 255));
        return animation;
    }

    private static ManagedKittyAnimation Create(KittyGraphicsDecodedImage root) => new(new ManagedKittyImagePixels(root));

    private static KittyGraphicsDecodedImage Pixel(byte red, byte green, byte blue) => new(1, 1, [red, green, blue, 255]);

    private static ManagedKittyGraphicsCommand Command(string text)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 4096, out ManagedKittyGraphicsCommand? command));
        return command;
    }

    private static void Append(ManagedKittyAnimation animation, string text, KittyGraphicsDecodedImage image) =>
        Assert.True(animation.TryTransmitFrame(Command(text), image, 1024 * 1024, out _, out _));
}
