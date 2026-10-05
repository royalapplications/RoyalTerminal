// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Native graphics_exec/storage commit RGB promotion before frame admission.
// Managed edits additionally need a COW canvas to preserve published readers;
// a failed COW allocation must retain old pixels, gaps and playback clocks.
public sealed class ManagedKittyAnimationFailureTests
{
    [Theory]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaBuffer, false)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaView, false)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaOwner, false)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaBuffer, true)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaView, true)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaOwner, true)]
    public void FailedPromotionRetainsRgbOwnerAndCanRetry(int checkpoint, bool compose)
    {
        ManagedKittyImagePixels original = ManagedKittyImagePixels.FromRgb(2, 1, [1, 2, 3, 4, 5, 6]);
        ManagedKittyAnimation animation = new(original);
        ManagedKittyGraphicsCommand command = Command(compose ? "a=c,r=1,c=1,w=1,h=1,x=1,C=1" : "a=f,X=1");
        Action<ManagedKittyAnimationAllocation> failure = stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); };
        string error;
        uint frame = 0;
        bool success = compose ? animation.TryCompose(command, out error, failure)
            : animation.TryTransmitFrame(command, Pixel(), 64, out frame, out error, failure);
        Assert.False(success);
        Assert.Equal("ENOMEM: out of memory", error);
        Assert.Equal(0u, frame);
        Assert.Same(original, animation.CurrentPixels);
        Assert.Equal(6, animation.StoredBytes);
        Assert.Equal(1, animation.FrameCount);
        Assert.False(original.IsRgba);
        Assert.True(compose ? animation.TryCompose(command, out error)
            : animation.TryTransmitFrame(command, Pixel(), 64, out frame, out error), error);
        Assert.True(animation.CurrentPixels.IsRgba);
        Assert.Equal(compose ? 8 : 16, animation.StoredBytes);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, original.Data.ToArray());
    }

    public static IEnumerable<object[]> FrameFailures()
    {
        foreach (string control in new[] { "a=f,z=99", "a=f,c=1,z=99", "a=f,r=1,z=99" })
        {
            if (!control.Contains("r=1", StringComparison.Ordinal))
                yield return [control, (int)ManagedKittyAnimationAllocation.FrameCapacity];
            yield return [control, (int)ManagedKittyAnimationAllocation.FrameBuffer];
            yield return [control, (int)ManagedKittyAnimationAllocation.FrameView];
        }
    }

    [Theory]
    [MemberData(nameof(FrameFailures))]
    public void FailedFramePreparationPreservesPixelsGapsAndClock(string control, int checkpoint)
    {
        ManagedKittyAnimation animation = TwoFrames();
        ManagedKittyAnimation held = animation.CreateStateCopy();
        ManagedKittyImagePixels previous = animation.CurrentPixels;
        Assert.False(animation.TryTransmitFrame(Command(control), Pixel(), 64, out uint frame, out string error,
            stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); }));
        Assert.Equal(control.Contains("r=1", StringComparison.Ordinal) ? 1u : 3u, frame);
        Assert.Equal("ENOMEM: out of memory", error);
        Assert.Equal(2, animation.FrameCount);
        Assert.Equal(16, animation.StoredBytes);
        Assert.Same(previous, animation.CurrentPixels);
        Assert.False(animation.Tick(105, true, out long? delay));
        Assert.Equal(5, delay);
        Assert.True(animation.TryTransmitFrame(Command(control), Pixel(), 64, out _, out _));
        Assert.Same(previous, held.CurrentPixels);
        Assert.False(held.Tick(105, true, out delay));
        Assert.Equal(5, delay);
    }

    [Theory]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer, 1)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer, 2)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView, 1)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView, 2)]
    public void FailedCompositionPreservesBothFrames(int checkpoint, int destination)
    {
        ManagedKittyAnimation animation = TwoFrames();
        ManagedKittyAnimation held = animation.CreateStateCopy();
        Assert.False(animation.TryCompose(Command($"a=c,r={(destination == 1 ? 2 : 1)},c={destination},C=1"), out string error,
            stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); }));
        Assert.Equal("ENOMEM: out of memory", error);
        Assert.False(animation.Tick(105, true, out long? delay));
        Assert.Equal(5, delay);
        for (int frame = 1; frame <= 2; frame++)
        {
            animation.ApplyControl(Command($"a=a,c={frame}"));
            held.ApplyControl(Command($"a=a,c={frame}"));
            Assert.Same(held.CurrentPixels, animation.CurrentPixels);
        }
    }

    [Theory]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaBuffer)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaView)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaOwner)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameCapacity)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView)]
    public void NonAllocationExceptionsAreNotSwallowed(int checkpoint)
    {
        ManagedKittyAnimation animation = new(ManagedKittyImagePixels.FromRgb(2, 1, [1, 2, 3, 4, 5, 6]));
        InvalidOperationException failure = new("Not an allocation failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => animation.TryTransmitFrame(Command("a=f"), Pixel(),
            64, out _, out _, stage => { if ((int)stage == checkpoint) throw failure; })));
        Assert.Equal(1, animation.FrameCount);
        Assert.Equal(checkpoint >= (int)ManagedKittyAnimationAllocation.FrameCapacity, animation.CurrentPixels.IsRgba);
    }

    [Theory]
    [InlineData("a=f,X=1,Y=4278190335")]
    [InlineData("a=f,X=1,c=1")]
    [InlineData("a=f,X=1,r=1,z=25")]
    public void FullCanvasOverwriteOwnsSourcePixelsAndPreservesReaders(string control)
    {
        ManagedKittyImagePixels original = new(Pixel());
        ManagedKittyAnimation animation = new(original);
        KittyGraphicsDecodedImage source = new(2, 1, [9, 8, 7, 6, 5, 4, 3, 2]);
        Assert.True(animation.TryTransmitFrame(Command(control), source, 64, out uint frame, out _));
        animation.ApplyControl(Command($"a=a,c={frame}"));
        source.Rgba[0] = 99;
        Assert.Equal(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }, animation.CurrentImage.Rgba);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, original.GetRgbaImage().Rgba);
    }

    [Theory]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer, true)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView, true)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer, false)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView, false)]
    public void FailedCompositionStillAccountsForCommittedRgbPromotion(int checkpoint, bool allocationFailure)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0, KittyGraphicsStorageLimitBytes = 10 });
        Send(processor, "a=T,i=1,f=24,s=2,v=1,C=1;AQIDBAUG");
        TerminalScreen held = screen.CreateStateCopy();
        Assert.True(held.TryGetKittyImageSource(1, out var previous));
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Exception failure = allocationFailure ? new OutOfMemoryException() : new InvalidOperationException();
        processor.KittyAnimationAllocationCheckpoint = stage => { if ((int)stage == checkpoint) throw failure; };
        if (allocationFailure)
        {
            Send(processor, "a=c,i=1,r=1,c=1,w=1,h=1,x=1,C=1");
            Assert.Equal("\u001b_Gi=1;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        }
        else Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Send(processor, "a=c,i=1,r=1,c=1,w=1,h=1,x=1,C=1")));
        Assert.True(processor.IsParserGround);
        processor.KittyAnimationAllocationCheckpoint = null;
        // Promotion raises image 1 from six to eight bytes. A new four-byte
        // image must evict it at limit ten, even though composition failed.
        Send(processor, "a=t,i=2,s=1,v=1;AQIDBA==");
        Send(processor, "a=p,i=1,C=1");
        Assert.Equal("\u001b_Gi=1;ENOENT: image not found\u001b\\", replies[^1]);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, previous!.RgbaPixels);
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaBuffer)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaView)]
    [InlineData((int)ManagedKittyAnimationAllocation.RgbaOwner)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameCapacity)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView)]
    public void ProcessorReportsStageCorrectFrameNumberAndConsumesFailedUpload(int checkpoint)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2), new() { ContinuationMaxBytes = 0 });
        Send(processor, "a=t,i=1,f=24,s=2,v=1;AQIDBAUG");
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.KittyAnimationAllocationCheckpoint = stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); };
        Send(processor, "a=f,i=1,f=24,s=1,v=1,m=1;AQI=");
        Send(processor, "m=0;Aw==");
        string frame = checkpoint >= (int)ManagedKittyAnimationAllocation.FrameCapacity ? ",r=2" : "";
        Assert.Equal($"\u001b_Gi=1{frame};ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        processor.KittyAnimationAllocationCheckpoint = null;
        Send(processor, "a=f,i=1,s=1,v=1;AQIDBA==");
        Assert.Equal("\u001b_Gi=1,r=2;OK\u001b\\", replies[^1]);
        Assert.True(processor.IsParserGround);
    }

    [Theory]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameCapacity)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameBuffer)]
    [InlineData((int)ManagedKittyAnimationAllocation.FrameView)]
    public void FailedFrameAllocationRetainsEvictionsButDoesNotChargeUnstoredCanvas(int checkpoint)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2),
            new() { ContinuationMaxBytes = 0, KittyGraphicsStorageLimitBytes = 12 });
        for (int id = 1; id <= 3; id++) Send(processor, $"a=T,i={id},s=1,v=1,C=1;AQIDBA==");
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.KittyAnimationAllocationCheckpoint = stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); };
        Send(processor, "a=f,i=1,s=1,v=1;BQYHCA==");
        Assert.Equal("\u001b_Gi=1,r=2;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        processor.KittyAnimationAllocationCheckpoint = null;
        Send(processor, "a=p,i=2,C=1");
        Assert.Equal("\u001b_Gi=2;ENOENT: image not found\u001b\\", replies[^1]);
        Send(processor, "a=t,i=4,s=1,v=1;AQIDBA==");
        Send(processor, "a=p,i=3,C=1");
        Assert.Equal("\u001b_Gi=3;OK\u001b\\", replies[^1]);
    }

    [Fact]
    public void AnimationErrorReplyObserverFailurePropagates()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2), new() { ContinuationMaxBytes = 0 });
        Send(processor, "a=t,i=1,s=2,v=1;AQIDBAUGBwg=");
        processor.KittyAnimationAllocationCheckpoint = stage =>
        {
            if (stage == ManagedKittyAnimationAllocation.FrameBuffer) throw new OutOfMemoryException("Canvas");
        };
        OutOfMemoryException failure = new("Reply observer");
        processor.ResponseCallback = _ => throw failure;
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Send(processor, "a=f,i=1,s=1,v=1;AQIDBA==")));
    }

    private static ManagedKittyAnimation TwoFrames()
    {
        ManagedKittyAnimation animation = new(new ManagedKittyImagePixels(Pixel()));
        Assert.True(animation.TryTransmitFrame(Command("a=f,z=20"), Pixel(), 64, out _, out _));
        animation.ApplyControl(Command("a=a,r=1,z=10,s=3"));
        Assert.False(animation.Tick(100, true, out _));
        return animation;
    }

    private static KittyGraphicsDecodedImage Pixel() => new(2, 1, [1, 2, 3, 255, 4, 5, 6, 255]);

    private static ManagedKittyGraphicsCommand Command(string input)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(input), 0, out var command));
        return command;
    }

    private static void Send(BasicVtProcessor processor, string command)
        => processor.Process(Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\"));
}
