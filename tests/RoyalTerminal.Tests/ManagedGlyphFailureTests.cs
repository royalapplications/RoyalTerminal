// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Glyphs;
using Xunit;

namespace RoyalTerminal.Tests;

// Reference: Ghostty apc.Handler abandons failed glyph command buffers;
// glyph.execute wraps register failures as out_of_memory, and Glossary.register
// reserves before FIFO edits. Terminal.glyphProtocol marks dirty before replies.
public sealed class ManagedGlyphFailureTests
{
    private const string EmptyGlyph = "AAAAAAAAAAAAAA==";

    public static IEnumerable<object[]> RegistrationFailures()
    {
        foreach (TerminalGlyphAllocation stage in new[]
        {
            TerminalGlyphAllocation.DecodeBuffer, TerminalGlyphAllocation.Outline,
            TerminalGlyphAllocation.Registration, TerminalGlyphAllocation.Glossary,
            TerminalGlyphAllocation.RegistryCapacity, TerminalGlyphAllocation.OrderCapacity,
        })
        for (int verbosity = 0; verbosity < 3; verbosity++) yield return [(int)stage, verbosity];
    }

    [Theory]
    [MemberData(nameof(RegistrationFailures))]
    public void RegistrationAllocationFailureRepliesAccordingToVerbosityAndRecovers(int checkpoint, int verbosity)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = Create(screen);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.GlyphAllocationCheckpoint = stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); };
        Send(processor, $"r;cp=e000;reply={verbosity};{EmptyGlyph}");
        if (verbosity == 0) Assert.Empty(replies);
        else Assert.Equal("\u001b_25a1;r;cp=e000;status=1;reason=out_of_memory\u001b\\", Assert.Single(replies));
        Assert.Equal(0, screen.RegisteredGlyphCount);
        Assert.Equal(1UL, screen.GlyphRevision);
        Assert.False(screen.SnapshotMutationFailed);
        processor.GlyphAllocationCheckpoint = null;
        Send(processor, "r;cp=e000;" + EmptyGlyph);
        Assert.Equal(1, screen.RegisteredGlyphCount);
        Assert.Equal(2UL, screen.GlyphRevision);
        Assert.Equal("\u001b_25a1;r;cp=e000;status=0\u001b\\", replies[^1]);
    }

    [Theory]
    [InlineData(0, (int)TerminalGlyphAllocation.RegistryCapacity)]
    [InlineData(0, (int)TerminalGlyphAllocation.OrderCapacity)]
    [InlineData(4, (int)TerminalGlyphAllocation.RegistryCapacity)]
    [InlineData(4, (int)TerminalGlyphAllocation.OrderCapacity)]
    [InlineData(1024, (int)TerminalGlyphAllocation.RegistryCapacity)]
    [InlineData(1024, (int)TerminalGlyphAllocation.OrderCapacity)]
    public void GlossaryReservationFailurePreservesEntriesAndEvictionOrder(int count, int checkpoint)
    {
        TerminalGlyphGlossary glossary = new();
        TerminalGlyphRegistration entry = new(new([], []), 1000, 1000, 1000, 1, default);
        for (uint i = 0; i < count; i++) glossary.Register(0xE000 + i, entry);
        TerminalGlyphGlossary retained = glossary.Copy();
        OutOfMemoryException failure = new("FIFO reservation failure");
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => glossary.Register(0xF000, entry,
            stage => { if ((int)stage == checkpoint) throw failure; })));
        Assert.Equal(count, glossary.Count);
        Assert.False(glossary.TryGet(0xF000, out _));
        for (uint i = 0; i < count; i++) Assert.True(glossary.TryGet(0xE000 + i, out _));
        glossary.Register(0xF000, entry);
        Assert.Equal(Math.Min(count + 1, 1024), glossary.Count);
        if (count == 1024)
        {
            Assert.False(glossary.TryGet(0xE000, out _));
            // Existing-key replacement must not reserve or evict anything.
            glossary.Register(0xE001, entry, _ => throw new InvalidOperationException("Unexpected reservation."));
            glossary.Register(0xF001, entry);
            Assert.True(glossary.TryGet(0xE001, out _));
            Assert.False(glossary.TryGet(0xE002, out _));
            Assert.True(retained.TryGet(0xE000, out _));
        }
        Assert.Equal(count, retained.Count);
        Assert.False(retained.TryGet(0xF000, out _));
    }

    [Theory]
    [InlineData(false, 0x1B)]
    [InlineData(true, 0x1B)]
    [InlineData(false, 0x18)]
    [InlineData(true, 0x18)]
    [InlineData(false, 0x1A)]
    [InlineData(true, 0x1A)]
    public void BufferAllocationFailureDiscardsWholeCommandUntilExit(bool failAfterPrefix, int exit)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = Create(screen);
        List<byte[]> replies = [];
        List<TerminalUnknownSequence> unknown = [];
        processor.ResponseCallback = replies.Add;
        processor.UnknownSequenceCallback = unknown.Add;
        processor.Process("\u001b_25a1;"u8);
        if (failAfterPrefix) processor.Process("r;cp=e000;"u8);
        processor.GlyphAllocationCheckpoint = stage =>
        {
            if (stage == TerminalGlyphAllocation.CommandBuffer) throw new OutOfMemoryException();
        };
        if (!failAfterPrefix) processor.Process("r;cp=e000;"u8);
        processor.Process(Encoding.ASCII.GetBytes(EmptyGlyph));
        // Appending a second command-looking body may not revive the discarded command.
        processor.Process(";s;"u8);
        processor.Process(exit == 0x1B ? "\u001b\\"u8 : new byte[] { (byte)exit });
        Assert.Empty(replies);
        Assert.Empty(unknown);
        Assert.Equal(0, screen.RegisteredGlyphCount);
        Assert.Equal(0UL, screen.GlyphRevision);
        Assert.True(processor.GlyphCommandCapacity <= BasicVtProcessor.GlyphCommandRetainedBytes);
        processor.GlyphAllocationCheckpoint = null;
        Send(processor, "r;cp=e000;" + EmptyGlyph);
        Assert.Equal(1, screen.RegisteredGlyphCount);
    }

    [Fact]
    public void OversizedSliceIsRejectedBeforeAllocatingAnyBodyStorage()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = Create(screen);
        byte[] oversized = new byte[BasicVtProcessor.GlyphCommandMaximumBytes + 1];
        Array.Fill(oversized, (byte)'s');
        processor.GlyphAllocationCheckpoint = _ => throw new InvalidOperationException("Limit precedes allocation.");
        processor.Process("\u001b_25a1;"u8);
        processor.Process(oversized);
        processor.Process("\u001b\\"u8);
        Assert.Equal(0, processor.GlyphCommandCapacity);
        Assert.Equal(0UL, screen.GlyphRevision);
    }

    [Theory]
    [InlineData(0x1B)]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    public void LargeCommandStorageIsReleasedAtCompletionOrCancellation(int exit)
    {
        using BasicVtProcessor processor = Create(new(8, 2));
        processor.Process("\u001b_25a1;s;"u8);
        processor.Process(Encoding.ASCII.GetBytes(new string('x', 8000)));
        Assert.True(processor.GlyphCommandCapacity >= 8002);
        Assert.True(processor.GlyphCommandCapacity <= BasicVtProcessor.GlyphCommandMaximumBytes);
        processor.Process(exit == 0x1B ? "\u001b\\"u8 : new byte[] { (byte)exit });
        Assert.Equal(0, processor.GlyphCommandCapacity);
        Send(processor, "s");
        processor.GlyphAllocationCheckpoint = _ => throw new InvalidOperationException("Small scratch should be reused.");
        Send(processor, "s");
        Assert.True(processor.GlyphCommandCapacity <= BasicVtProcessor.GlyphCommandRetainedBytes);
    }

    [Theory]
    [InlineData("r;cp=e001;AAAAAAAAAAAAAA==", 2)]
    [InlineData("r;cp=1fffffz;AAAAAAAAAAAAAA==", 1)]
    [InlineData("c;cp=e000", 0)]
    public void DirtyRevisionIsPublishedBeforeObserverAndObserverFailureIsNotProtocolOom(string command, int count)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = Create(screen);
        Send(processor, "r;cp=e000;" + EmptyGlyph);
        ulong previous = screen.GlyphRevision;
        OutOfMemoryException observerFailure = new("Host observer, not protocol storage");
        byte[]? response = null;
        processor.ResponseCallback = bytes =>
        {
            response = bytes;
            Assert.Equal(previous + 1, screen.GlyphRevision);
            Assert.Equal(count, screen.RegisteredGlyphCount);
            throw observerFailure;
        };
        Assert.Same(observerFailure, Assert.Throws<OutOfMemoryException>(() => Send(processor, command)));
        Assert.Equal(count, screen.RegisteredGlyphCount);
        Assert.DoesNotContain("out_of_memory", Encoding.ASCII.GetString(response!));
        if (command.Contains("1fffffz", StringComparison.Ordinal))
            Assert.Equal("\u001b_25a1;r;cp=0;status=1;reason=malformed_payload\u001b\\", Encoding.ASCII.GetString(response!));
        Assert.False(screen.SnapshotMutationFailed);
        processor.ResponseCallback = null;
        Send(processor, "s");
        Assert.Equal(previous + 1, screen.GlyphRevision);
    }

    [Fact]
    public void EmptyQueriesAndClearNeedNoGlossaryOrReplyStorage()
    {
        TerminalGlyphGlossary? glossary = null;
        byte[][] commands = ["s"u8.ToArray(), "q;cp=e000"u8.ToArray(), "c"u8.ToArray()];
        foreach (byte[] command in commands) _ = ManagedGlyphProtocol.Execute(command, ref glossary);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        foreach (byte[] command in commands)
        {
            ManagedGlyphProtocol.Result result = ManagedGlyphProtocol.Execute(command, ref glossary);
            ManagedGlyphProtocol.Send(in result, null, null);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Null(glossary);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NonAllocationFailurePropagatesWithoutInventingOomResponse()
    {
        TerminalGlyphGlossary? glossary = null;
        InvalidOperationException failure = new("Not an allocation failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ManagedGlyphProtocol.Execute(
            "r;cp=e000;AAAAAAAAAAAAAA=="u8, ref glossary, _ => throw failure)));
        Assert.Null(glossary);
    }

    private static BasicVtProcessor Create(TerminalScreen screen) => new(screen,
        new() { GlyphProtocolEnabled = true, ContinuationMaxBytes = 0 });
    private static void Send(BasicVtProcessor processor, string command)
        => processor.Process(Encoding.ASCII.GetBytes("\u001b_25a1;" + command + "\u001b\\"));
}
