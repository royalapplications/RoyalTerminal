// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty graphics_exec.encodeError/loadAndAddImage and LoadingImage.addData:
// failed reservation keeps previous bytes and the new nonzero quiet policy;
// completion consumes the pending loader before it can fail. xterm.js image
// storage and Windows Terminal's SIXEL/ImageSlice do not define this contract.
public sealed class ManagedKittyImageFailureTests
{
    [Theory]
    [InlineData((int)ManagedKittyImageAllocation.Loader)]
    [InlineData((int)ManagedKittyImageAllocation.BufferCopy)]
    public void CreationFailureReturnsNoLoader(int checkpoint)
    {
        ManagedKittyGraphicsCommand command = Command("i=73,s=1,v=1");
        command.SetData(new byte[] { 0, 1, 2, 3, 4 }.AsMemory(1));
        Assert.False(ManagedKittyImageLoader.TryCreate(command, null, 1024, out var loader, out string error,
            allocationCheckpoint: stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); }));
        Assert.Null(loader);
        Assert.Equal("ENOMEM: out of memory", error);
    }

    [Theory]
    [InlineData("f=99", "EINVAL: unsupported format")]
    [InlineData("f=32", "EINVAL: invalid data")]
    public void AdmissionErrorsPrecedeAllocation(string control, string expected)
    {
        ManagedKittyGraphicsCommand command = Command(control);
        command.SetData(new byte[5]);
        Assert.False(ManagedKittyImageLoader.TryCreate(command, null, 4, out _, out string error,
            allocationCheckpoint: _ => throw new InvalidOperationException("No allocation expected")));
        Assert.Equal(expected, error);
    }

    public static IEnumerable<object[]> CompletionFailures()
    {
        yield return [(int)ManagedKittyImageAllocation.BufferTransfer, 32, false];
        yield return [(int)ManagedKittyImageAllocation.Pixels, 24, false];
        yield return [(int)ManagedKittyImageAllocation.Pixels, 32, false];
        yield return [(int)ManagedKittyImageAllocation.Pixels, 100, false];
        yield return [(int)ManagedKittyImageAllocation.InflateStream, 32, true];
        yield return [(int)ManagedKittyImageAllocation.InflateScratch, 32, true];
        yield return [(int)ManagedKittyImageAllocation.BufferGrowth, 32, true];
        yield return [(int)ManagedKittyImageAllocation.BufferTransfer, 32, true];
        yield return [(int)ManagedKittyImageAllocation.Pixels, 32, true];
    }

    [Theory]
    [MemberData(nameof(CompletionFailures))]
    public void CompletionAllocationFailuresReturnEnomemNotInvalidData(int checkpoint, int format, bool compressed)
    {
        ManagedKittyGraphicsCommand command = CompletionCommand(format, compressed);
        Assert.True(ManagedKittyImageLoader.TryCreate(command, new Decoder(), 1024, out var loader, out _,
            allocationCheckpoint: stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); }));
        Assert.False(loader.TryComplete(out var image, out string error));
        Assert.Null(image);
        Assert.Equal("ENOMEM: out of memory", error);
    }

    [Theory]
    [MemberData(nameof(CompletionFailures))]
    public void NonAllocationCompletionFailuresStillPropagate(int checkpoint, int format, bool compressed)
    {
        InvalidOperationException failure = new("Not an allocation failure");
        Assert.True(ManagedKittyImageLoader.TryCreate(CompletionCommand(format, compressed), new Decoder(), 1024,
            out var loader, out _, allocationCheckpoint: stage => { if ((int)stage == checkpoint) throw failure; }));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => loader.TryComplete(out _, out _)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void DomainProvidersMapOnlyAllocationFailures(bool png, bool outOfMemory)
    {
        Exception failure = outOfMemory ? new OutOfMemoryException() : new InvalidOperationException();
        ManagedKittyGraphicsCommand command = Command(png ? "f=100" : "t=f,s=1,v=1");
        bool Complete(out string error)
        {
            return ManagedKittyImageLoader.TryCreate(command, new Decoder(failure), 1024, out var loader,
                out error, new MediumReader(failure)) && loader.TryComplete(out _, out error);
        }
        if (outOfMemory)
        {
            Assert.False(Complete(out string error));
            Assert.Equal("ENOMEM: out of memory", error);
        }
        else Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Complete(out _)));
    }

    public static IEnumerable<object[]> FailedChunks()
    {
        for (int initialQuiet = 0; initialQuiet < 3; initialQuiet++)
        for (int overrideQuiet = 0; overrideQuiet < 3; overrideQuiet++)
        foreach (bool more in new[] { false, true }) yield return [initialQuiet, overrideQuiet, more];
    }

    [Theory]
    [MemberData(nameof(FailedChunks))]
    public void FailedAppendRetainsBytesAndNonzeroQuietOverride(int initialQuiet, int overrideQuiet, bool more)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0, KittyGraphicsMaxImageBytes = 4 });
        bool fail = false;
        processor.KittyImageAllocationCheckpoint = stage =>
        {
            if (fail && stage == ManagedKittyImageAllocation.BufferGrowth) throw new OutOfMemoryException();
        };
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Send(processor, $"a=T,i=73,s=1,v=1,C=1,m=1,q={initialQuiet};/wAA");
        fail = true;
        Send(processor, $"m={(more ? 1 : 0)},q={overrideQuiet};/w==");
        int quiet = overrideQuiet == 0 ? initialQuiet : overrideQuiet;
        if (quiet == 2) Assert.Empty(replies);
        else Assert.Equal("\u001b_Gi=73;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        Assert.True(processor.IsParserGround);
        Assert.False(screen.TryGetKittyImageSource(73, out _));
        replies.Clear();
        fail = false;
        Send(processor, "m=0;/w==");
        if (quiet == 0) Assert.Equal("\u001b_Gi=73;OK\u001b\\", Assert.Single(replies));
        else Assert.Empty(replies);
        Assert.True(screen.TryGetKittyImageSource(73, out var image));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image!.RgbaPixels);
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData(false, (int)ManagedKittyImageAllocation.BufferTransfer)]
    [InlineData(true, (int)ManagedKittyImageAllocation.BufferTransfer)]
    [InlineData(false, (int)ManagedKittyImageAllocation.Pixels)]
    [InlineData(true, (int)ManagedKittyImageAllocation.Pixels)]
    public void FailedFinalDecodeConsumesPendingImageOrFrame(bool frame, int checkpoint)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0, KittyGraphicsMaxImageBytes = 1024 });
        Send(processor, "a=T,i=73,s=1,v=1,C=1;/wAA/w==");
        Assert.True(screen.TryGetKittyImageSource(73, out var retained));
        bool fail = false;
        processor.KittyImageAllocationCheckpoint = stage =>
        {
            if (fail && (int)stage == checkpoint) throw new OutOfMemoryException();
        };
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Send(processor, $"a={(frame ? 'f' : 'T')},i=73,s=1,v=1,C=1,m=1;AQID");
        fail = true;
        Send(processor, "m=0;BA==");
        Assert.Equal("\u001b_Gi=73;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        Assert.Equal(frame, screen.TryGetKittyImageSource(73, out _));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, retained!.RgbaPixels);
        Assert.True(processor.IsParserGround);
        Assert.False(screen.SnapshotMutationFailed);
        // This must be a new transfer, not a continuation of the consumed one.
        fail = false;
        replies.Clear();
        Send(processor, "a=T,i=74,s=1,v=1,C=1;AQIDBA==");
        Assert.Equal("\u001b_Gi=74;OK\u001b\\", Assert.Single(replies));
        Assert.True(screen.TryGetKittyImageSource(74, out var image));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image!.RgbaPixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedQueryDoesNotConsumePendingTransferOrChangeItsQuietPolicy(int quiet)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Send(processor, $"a=T,i=73,s=1,v=1,C=1,m=1,q={quiet};AQID");
        processor.KittyImageAllocationCheckpoint = _ => throw new OutOfMemoryException();
        Send(processor, "a=q,i=74,s=1,v=1;AQIDBA==");
        Assert.Equal("\u001b_Gi=74;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        processor.KittyImageAllocationCheckpoint = null;
        replies.Clear();
        Send(processor, "m=0;BA==");
        if (quiet == 0) Assert.Equal("\u001b_Gi=73;OK\u001b\\", Assert.Single(replies));
        else Assert.Empty(replies);
        Assert.True(screen.TryGetKittyImageSource(73, out var image));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image!.RgbaPixels);
    }

    [Fact]
    public void InitialFrameLoaderFailureEchoesRequestedFrameAndDoesNotChangeImage()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
        Send(processor, "a=T,i=73,s=1,v=1,C=1;/wAA/w==");
        Assert.True(screen.TryGetKittyImageSource(73, out var retained));
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.KittyImageAllocationCheckpoint = _ => throw new OutOfMemoryException();
        Send(processor, "a=f,i=73,r=4,s=1,v=1;AQIDBA==");
        Assert.Equal("\u001b_Gi=73,r=4;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        Assert.True(screen.TryGetKittyImageSource(73, out var current));
        Assert.Same(retained, current);
    }

    [Theory]
    [InlineData((int)ManagedKittyImageAllocation.Loader)]
    [InlineData((int)ManagedKittyImageAllocation.BufferCopy)]
    public void NonAllocationCreationFailuresStillPropagate(int checkpoint)
    {
        InvalidOperationException failure = new("Not an allocation failure");
        ManagedKittyGraphicsCommand command = Command("s=1,v=1");
        command.SetData(new byte[] { 0, 1, 2, 3, 4 }.AsMemory(1));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            ManagedKittyImageLoader.TryCreate(command, null, 1024, out _, out _,
                allocationCheckpoint: stage => { if ((int)stage == checkpoint) throw failure; })));
    }

    [Fact]
    public void NonAllocationAppendFailurePropagatesWithoutChangingPixels()
    {
        InvalidOperationException failure = new("Not an allocation failure");
        ManagedKittyGraphicsCommand command = Command("s=1,v=1");
        command.SetData(new byte[] { 1, 2 });
        bool fail = true;
        Assert.True(ManagedKittyImageLoader.TryCreate(command, null, 4, out var loader, out _,
            allocationCheckpoint: stage => { if (fail && stage == ManagedKittyImageAllocation.BufferGrowth) throw failure; }));
        ManagedKittyGraphicsCommand continuation = Command("q=1");
        continuation.SetData(new byte[] { 3, 4 });
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => loader.TryAppend(continuation, out _)));
        Assert.Equal(1, loader.Quiet);
        fail = false;
        Assert.True(loader.TryAppend(continuation, out _));
        Assert.True(loader.TryComplete(out var image, out _));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.Data.ToArray());
    }

    [Theory]
    [InlineData("q", false)]
    [InlineData("t", true)]
    [InlineData("T", true)]
    public void CreationFailurePreservesQueryImageButRetiresExplicitRetransmission(string action, bool deleted)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
        Send(processor, "a=T,i=73,s=1,v=1,C=1;/wAA/w==");
        Assert.True(screen.TryGetKittyImageSource(73, out var retained));
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.KittyImageAllocationCheckpoint = stage =>
        {
            if (stage == ManagedKittyImageAllocation.Loader) throw new OutOfMemoryException();
        };
        Send(processor, $"a={action},i=73,s=1,v=1,C=1;/wAA/w==");
        Assert.Equal("\u001b_Gi=73;ENOMEM: out of memory\u001b\\", Assert.Single(replies));
        Assert.Equal(!deleted, screen.TryGetKittyImageSource(73, out _));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, retained!.RgbaPixels);
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Fact]
    public void ErrorReplyObserverFailureIsNotSwallowed()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2), new() { ContinuationMaxBytes = 0 });
        processor.KittyImageAllocationCheckpoint = _ => throw new OutOfMemoryException("Loader");
        OutOfMemoryException failure = new("Host response observer");
        processor.ResponseCallback = _ => throw failure;
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Send(processor, "a=q,i=73,s=1,v=1;/wAA/w==")));
        Assert.True(processor.IsParserGround);
    }

    private static ManagedKittyGraphicsCommand CompletionCommand(int format, bool compressed)
    {
        ManagedKittyGraphicsCommand command = Command($"f={format},s=1,v=1" + (compressed ? ",o=z" : ""));
        if (compressed)
        {
            using MemoryStream output = new();
            using (ZLibStream stream = new(output, CompressionLevel.Fastest, leaveOpen: true)) stream.Write([1, 2, 3, 4]);
            command.SetData(output.ToArray());
        }
        else command.SetData(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0 }.AsMemory(0, format == 24 ? 3 : 4));
        return command;
    }

    private sealed class Decoder(Exception? failure = null) : IKittyGraphicsPngDecoder
    {
        public bool TryDecode(ReadOnlySpan<byte> encoded, int maxDecodedBytes,
            [NotNullWhen(true)] out KittyGraphicsDecodedImage? image)
        {
            if (failure is not null) throw failure;
            image = new(1, 1, [1, 2, 3, 4]);
            return true;
        }
    }

    private sealed class MediumReader(Exception failure) : IKittyGraphicsMediumReader
    {
        public bool TryRead(KittyGraphicsMediumRequest request, int maxBytes, [NotNullWhen(true)] out byte[]? data, out string? error)
            => throw failure;
    }

    private static ManagedKittyGraphicsCommand Command(string command)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(command), 1024, out var parsed));
        return parsed;
    }

    private static void Send(BasicVtProcessor processor, string command)
        => processor.Process(Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\"));
}
