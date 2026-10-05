// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty distinguishes recoverable page-capacity refusal from a fatal
// partially mutated clone. WT ROW::_resizeChars prepares backing storage
// before installing it; xterm.js BufferLine owns separate combined strings.
// Neither exposes PAGE metadata. CLR allocation failures cannot be mistaken
// for a Ghostty capacity refusal: contain them to the managed mutation owner.
public sealed class ManagedSnapshotMutationFailureTests
{
    [Theory]
    [InlineData((int)SnapshotMutationCheckpoint.MetadataWrite, "A", "XYZ")]
    [InlineData((int)SnapshotMutationCheckpoint.MetadataWrite, "A", "X")]
    [InlineData((int)SnapshotMutationCheckpoint.MetadataClear, "A\u0301", "\r\u001b[K")]
    [InlineData((int)SnapshotMutationCheckpoint.MetadataClear, "A\u0301", "\u001b[2J")]
    [InlineData((int)SnapshotMutationCheckpoint.MetadataClear, "ABCDE", "\r\u001b[@")]
    [InlineData((int)SnapshotMutationCheckpoint.MetadataClear, "ABCDE", "\r\u001b[P")]
    [InlineData((int)SnapshotMutationCheckpoint.GraphemeAppend, "A", "\u0301")]
    [InlineData((int)SnapshotMutationCheckpoint.Revision, "A", "XYZ")]
    public void PartialMutationFaultsOnlyItsOwnerBeforeCleanup(int checkpoint, string seed, string command)
    {
        TerminalScreen screen = CreateScreen();
        using BasicVtProcessor processor = new(screen);
        Write(processor, seed);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalCell[] original = retained.GetViewportRow(0).ReadOnlyCells.ToArray();
        OutOfMemoryException failure = new("Injected partial metadata allocation failure");
        byte[] input = Encoding.UTF8.GetBytes(command);
        bool injected = false;
        int cleanupAfterFailure = 0;
        screen.MutationCheckpoint = phase =>
        {
            if (injected && phase == SnapshotMutationCheckpoint.Revision) cleanupAfterFailure++;
            if ((int)phase != checkpoint || injected) return;
            injected = true;
            throw failure;
        };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process(input)));

        Assert.True(injected);
        Assert.Equal(0, cleanupAfterFailure);
        Assert.True(screen.SnapshotMutationFailed);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(original, retained.GetViewportRow(0).ReadOnlyCells.ToArray());
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("later"u8)));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.GetBinarySnapshot()));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(processor.Reset));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(8, 2, 80, 40)));
        TerminalScreen copiedFault = screen.CreateStateCopy();
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(copiedFault.ThrowIfSnapshotMutationFailed));
        using BasicVtProcessor independent = new(retained);
        independent.Process("usable"u8);
        Assert.NotEmpty(independent.GetBinarySnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingFirstFailureAllocatesNothingAndRetainsFirstCause(bool tracked)
    {
        // Warm both latching paths without sharing the measured owner's state.
        TerminalScreen warm = tracked ? CreateScreen() : new(4, 1);
        if (tracked) { using var edit = warm.EditSnapshotRowMetadata(warm.GetViewportRow(0)); }
        OutOfMemoryException first = new("first"), later = new("later");
        warm.RecordSnapshotMutationFailure(first);
        TerminalScreen screen = tracked ? CreateScreen() : new(4, 1);
        if (tracked) { using var edit = screen.EditSnapshotRowMetadata(screen.GetViewportRow(0)); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        screen.RecordSnapshotMutationFailure(first);
        screen.RecordSnapshotMutationFailure(later);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Same(first, Assert.Throws<OutOfMemoryException>(screen.ThrowIfSnapshotMutationFailed));
        TerminalScreen copy = screen.CreateStateCopy();
        Assert.Same(first, Assert.Throws<OutOfMemoryException>(copy.ThrowIfSnapshotMutationFailed));
    }

    [Fact]
    public void TrackerFailureLatchDoesNotAllocateOrReplaceFirstCause()
    {
        OutOfMemoryException first = new("first"), later = new("later");
        GhosttySnapshotPageTracker warm = new();
        warm.RecordMutationFailure(first);
        GhosttySnapshotPageTracker tracker = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        tracker.RecordMutationFailure(first);
        tracker.RecordMutationFailure(later);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Same(first, tracker.MutationFailure);
        Assert.Same(first, Assert.Throws<OutOfMemoryException>(tracker.ThrowIfMutationFailed));
    }

    [Fact]
    public void FailedHeldPayloadCannotPublishOnRefreshOrDispose()
    {
        TerminalScreen screen = CreateScreen();
        using BasicVtProcessor processor = new(screen);
        processor.Process("visible"u8);
        TerminalRow published = screen.GetViewportRow(0);
        TerminalCell[] cells = published.ReadOnlyCells.ToArray();
        OutOfMemoryException failure = new("Injected held write failure");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.MetadataWrite) throw failure;
        };
        processor.Process("\u001b[?2026h"u8);

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("hidden"u8)));

        Assert.False(screen.SnapshotMutationFailed);
        Assert.False(processor.RefreshTimedState());
        Assert.Null(processor.NextTimedRefreshDelay);
        processor.Dispose();
        Assert.Same(published, screen.GetViewportRow(0));
        Assert.Equal(cells, published.ReadOnlyCells.ToArray());
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("response")]
    [InlineData("bell")]
    public void HostCallbackAllocationFailureDoesNotFaultValidTerminal(string callback)
    {
        TerminalScreen screen = CreateScreen();
        using BasicVtProcessor processor = new(screen);
        processor.Process("ready"u8);
        OutOfMemoryException failure = new("Host observer failure");
        string command;
        switch (callback)
        {
            case "title": processor.TitleCallback = _ => throw failure; command = "\u001b]2;title\a"; break;
            case "response": processor.ResponseCallback = _ => throw failure; command = "\u001b[5n"; break;
            default: processor.BellCallback = () => throw failure; command = "\a"; break;
        }

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Write(processor, command)));

        Assert.False(screen.SnapshotMutationFailed);
        processor.TitleCallback = null;
        processor.ResponseCallback = null;
        processor.BellCallback = null;
        processor.Process("\u0018X"u8);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    [Fact]
    public void ResizeMetadataFailureRollsBackWithoutFaultingOriginalOwner()
    {
        TerminalScreen screen = CreateScreen();
        using BasicVtProcessor processor = new(screen);
        Write(processor, "\u001b]133;A;redraw=last\aPROMPT");
        byte[] before = processor.GetBinarySnapshot();
        OutOfMemoryException failure = new("Injected staged prompt clear failure");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.MetadataClear) throw failure;
        };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(6, 2, 60, 40)));

        Assert.False(screen.SnapshotMutationFailed);
        screen.MutationCheckpoint = null;
        Assert.Equal(before, processor.GetBinarySnapshot());
        processor.ResizeScreen(6, 2, 60, 40);
        processor.Process("still usable"u8);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    [Theory]
    [InlineData("\r\u001b[@", " ABCDE")]
    [InlineData("\r\u001b[P", "BCDE ")]
    public void CharacterShiftsDetachOnceAndKeepRetainedCells(string command, string expected)
    {
        TerminalScreen screen = CreateScreen();
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABCDE"u8);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow row = screen.GetViewportRow(0);
        object shared = row.SearchStorageIdentity;
        Write(processor, command);
        Assert.NotSame(shared, row.SearchStorageIdentity);
        Assert.Same(shared, retained.GetViewportRow(0).SearchStorageIdentity);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i] == ' ' ? 0 : expected[i], row.ReadOnlyCells[i].Codepoint);
        for (int i = 0; i < 5; i++) Assert.Equal("ABCDE"[i], retained.GetViewportRow(0).ReadOnlyCells[i].Codepoint);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    private static TerminalScreen CreateScreen()
        => new(12, 3, 20) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };

    private static void Write(BasicVtProcessor processor, string value) => processor.Process(Encoding.UTF8.GetBytes(value));
}
