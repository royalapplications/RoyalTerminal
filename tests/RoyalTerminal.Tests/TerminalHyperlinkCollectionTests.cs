// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.endHyperlink releases cursor ownership; RefCountedSet retains
// zero-reference entries until pressure prunes them. WT TextBuffer::_PruneHyperlinks
// checks surviving buffer rows, while xterm.js OscLinkService tracks line markers.
// Royal's host-token registry is distinct from the exact page allocator: prune
// only protocol-owned tokens, amortize scans, retain hidden columns/both buffers/
// cursor roots, and preserve the public stable-token contract. COW readers keep
// independent registries. No change to logical snapshot page quota accounting.
public sealed class TerminalHyperlinkCollectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuccessfulUnprintedIdentitiesAreBoundedInsideOneInputBatch(bool explicitId, bool held)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = new FrozenClock() });
        Write(processor, Open("first", explicitId ? "first" : null) + Close);
        Assert.True(screen.TryGetHyperlink(1, out _));
        if (held) Write(processor, "\u001b[?2026h");
        StringBuilder input = new();
        for (int index = 0; index < 1024; index++) input.Append(Open("u" + index, explicitId ? "i" + index : null)).Append(Close);
        Write(processor, input.ToString());
        if (held)
        {
            Assert.True(screen.TryGetHyperlink(1, out _));
            Write(processor, "\u001b[?2026l");
        }
        Assert.False(screen.TryGetHyperlink(1, out _));
        Assert.False(screen.TryGetHyperlinkUrl(1, out _));
        int survivors = 0;
        for (int token = 1; token <= 1025; token++) if (screen.TryGetHyperlink(token, out _)) survivors++;
        Assert.InRange(survivors, 0, TerminalHyperlinkRegistry.CollectionSlack);
        Write(processor, Open("tail") + "A");
        int tail = screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        Assert.True(tail > 1025); // Pressure pruning never recycles published tokens.
        Assert.True(screen.TryGetHyperlink(tail, out TerminalHyperlink? link));
        Assert.Equal(explicitId ? 0U : 1025U, link!.ImplicitId);
    }

    [Fact]
    public void BothBuffersAndUnprintedCursorRemainRootsUnderPressure()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        Write(processor, Open("primary") + "P" + Close + "\u001b[?47h" + Open("alternate") + "A" + Close + "\u001b[?47l");
        int primary = screen.GetSnapshotRows(0)![0].ReadOnlyCells[0].HyperlinkId;
        int alternate = screen.GetSnapshotRows(1)![0].ReadOnlyCells[0].HyperlinkId;
        Churn(processor, 768);
        Write(processor, Open("cursor"));
        int cursor = screen.SnapshotCursorHyperlinkToken(0, 0);
        Assert.NotEqual(0, cursor);
        screen.CollectUnusedHyperlinks([], force: true);
        foreach (int token in new[] { primary, alternate, cursor }) Assert.True(screen.TryGetHyperlink(token, out _));
        Write(processor, "C");
        Assert.Equal(cursor, screen.GetSnapshotRows(0)![0].ReadOnlyCells[1].HyperlinkId);
    }

    [Fact]
    public void HiddenColumnsAndCallerCursorTokensAreRoots()
    {
        TerminalScreen screen = new(8, 2);
        int hidden = Owned(screen, "hidden"), cursor = Owned(screen, "cursor"), dead = Owned(screen, "dead");
        TerminalRow row = screen.GetViewportRow(0);
        row.Cells[7].HyperlinkId = hidden;
        row.Resize(2);
        Assert.Equal(1, screen.CollectUnusedHyperlinks([cursor], force: true));
        Assert.True(screen.TryGetHyperlink(hidden, out _));
        Assert.True(screen.TryGetHyperlink(cursor, out _));
        Assert.False(screen.TryGetHyperlink(dead, out _));
        row.Resize(8);
        Assert.Equal(hidden, row.ReadOnlyCells[7].HyperlinkId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicRegistrationsStayStableIncludingPromotionOfAnOwnedIdentity(bool promote)
    {
        TerminalScreen screen = new(8, 2);
        if (promote) _ = Owned(screen, "public");
        int bytesToken = screen.RegisterHyperlink("public"u8, [], 0);
        int urlToken = screen.RegisterHyperlink("legacy");
        int dead = Owned(screen, "dead");
        Assert.Equal(1, screen.CollectUnusedHyperlinks([], force: true));
        Assert.False(screen.TryGetHyperlink(dead, out _));
        Assert.Equal(bytesToken, screen.RegisterHyperlink("public"u8, [], 0));
        Assert.True(screen.TryGetHyperlinkUrl(bytesToken, out _));
        Assert.Equal(urlToken, screen.RegisterHyperlink("legacy"));
    }

    [Fact]
    public void CowCollectionAndPublicPromotionDoNotChangeRetainedOwners()
    {
        TerminalScreen screen = new(8, 2);
        int token = Owned(screen, "owned");
        screen.GetViewportRow(0).Cells[0].HyperlinkId = token;
        TerminalScreen copy = screen.CreateStateCopy();
        screen.GetViewportRow(0).Cells[0].HyperlinkId = 0;
        Assert.Equal(1, screen.CollectUnusedHyperlinks([], force: true));
        Assert.True(copy.TryGetHyperlink(token, out _));
        Assert.False(screen.TryGetHyperlink(token, out _));
        Assert.Equal(token, copy.RegisterHyperlink("owned"u8, [], 0));
        copy.GetViewportRow(0).Cells[0].HyperlinkId = 0;
        Assert.Equal(0, copy.CollectUnusedHyperlinks([], force: true));
        Assert.True(copy.TryGetHyperlink(token, out _));
    }

    [Fact]
    public void PreparedCollectionFailureIsAtomicAndRetryable()
    {
        TerminalScreen screen = new(8, 2);
        int dead = Owned(screen, "dead"), live = Owned(screen, "live");
        screen.GetViewportRow(0).Cells[0].HyperlinkId = live;
        TerminalScreen retained = screen.CreateStateCopy();
        OutOfMemoryException failure = new("Before hyperlink registry swap");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.HyperlinkCollectionPrepared) throw failure;
        };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.CollectUnusedHyperlinks([], force: true)));
        Assert.False(screen.SnapshotMutationFailed);
        foreach (int token in new[] { dead, live })
        {
            Assert.True(screen.TryGetHyperlink(token, out _));
            Assert.True(screen.TryGetHyperlinkUrl(token, out _));
        }
        screen.MutationCheckpoint = null;
        Assert.Equal(1, screen.CollectUnusedHyperlinks([], force: true));
        Assert.False(screen.TryGetHyperlink(dead, out _));
        Assert.True(screen.TryGetHyperlink(live, out _));
        Assert.True(retained.TryGetHyperlink(dead, out _));
    }

    [Fact]
    public void OptionalCleanupOomDoesNotFaultSuccessfullyProcessedInput()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.HyperlinkCollectionPrepared) throw new OutOfMemoryException();
        };
        Churn(processor, TerminalHyperlinkRegistry.CollectionSlack + 2);
        Assert.False(screen.SnapshotMutationFailed);
        Assert.True(screen.TryGetHyperlink(1, out _));
        screen.MutationCheckpoint = null;
        processor.Process([]);
        Assert.False(screen.TryGetHyperlink(1, out _));
        processor.Process("A"u8);
        Assert.Equal('A', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void EvictedHistoryIsNotARegistryRoot()
    {
        TerminalScreen screen = new(8, 2, 1);
        using BasicVtProcessor processor = new(screen);
        Write(processor, Open("history") + "H" + Close);
        int token = screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        for (int index = 0; index < 8; index++) processor.Process("\r\nrow"u8);
        screen.CollectUnusedHyperlinks([], force: true);
        Assert.False(screen.TryGetHyperlink(token, out _));
    }

    [Fact]
    public void RestoredLinksAreCollectibleAfterErasureButRemainExportableWhileLive()
    {
        using BasicVtProcessor writer = new(new TerminalScreen(8, 2));
        Write(writer, Open("restored") + "R" + Close);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(writer.GetBinarySnapshot());
        int token = restored.Screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        Assert.Equal(0, restored.Screen.CollectUnusedHyperlinks([], force: true));
        using (ManagedTerminalSnapshot again = ManagedTerminalSnapshot.Restore(restored.Processor.GetBinarySnapshot()))
            Assert.Equal('R', again.Screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        restored.Processor.Process("\u001b[2J"u8);
        Assert.Equal(1, restored.Screen.CollectUnusedHyperlinks([], force: true));
        Assert.False(restored.Screen.TryGetHyperlink(token, out _));
    }

    [Fact]
    public void WarmBelowThresholdGateAllocatesNothingAndDoesNotScanRows()
    {
        TerminalScreen screen = new(8, 2);
        _ = Owned(screen, "unused");
        int checkpoints = 0;
        screen.MutationCheckpoint = _ => checkpoints++;
        for (int index = 0; index < 100; index++) screen.CollectUnusedHyperlinks([]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) screen.CollectUnusedHyperlinks([]);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(0, checkpoints);
    }

    [Fact]
    public void LiveHeavyCollectionBacksOffUntilRegistryGrowth()
    {
        TerminalScreen screen = new(300, 1);
        for (int index = 0; index < 256; index++) screen.GetViewportRow(0).Cells[index].HyperlinkId = Owned(screen, "u" + index);
        int checkpoints = 0;
        screen.MutationCheckpoint = _ => checkpoints++;
        Assert.Equal(0, screen.CollectUnusedHyperlinks([]));
        Assert.Equal(1, checkpoints);
        for (int index = 0; index < 100; index++) Assert.Equal(0, screen.CollectUnusedHyperlinks([]));
        Assert.Equal(1, checkpoints);
    }

    [Fact]
    public void SuccessfulCollectionDoesNotRetainRemovedValuesInScratch()
    {
        (TerminalScreen screen, WeakReference value) = CollectedValue();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(value.IsAlive);
        GC.KeepAlive(screen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void CollisionFilteringPreservesOrderAndOriginalCowChain(int mask)
    {
        TerminalHyperlink value = new("u"u8, [], 0);
        TerminalHyperlinkRegistry.Entry head = new(1, value, new(2, value, new(3, value, null)));
        HashSet<int> removed = [];
        List<int> expected = [];
        for (int token = 1; token <= 3; token++)
        {
            if ((mask & (1 << (token - 1))) != 0) removed.Add(token);
            else expected.Add(token);
        }
        List<TerminalHyperlinkRegistry.Entry> scratch = [];
        TerminalHyperlinkRegistry.Entry? result = TerminalHyperlinkRegistry.RemoveEntries(head, removed, scratch);
        List<int> actual = [];
        for (TerminalHyperlinkRegistry.Entry? entry = result; entry is not null; entry = entry.Next) actual.Add(entry.Token);
        Assert.Equal(expected, actual);
        Assert.Equal(2, head.Next!.Token);
        Assert.Equal(3, head.Next.Next!.Token);
        Assert.Empty(scratch);
        if (mask == 0) Assert.Same(head, result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TerminalScreen, WeakReference) CollectedValue()
    {
        TerminalScreen screen = new(8, 2);
        int token = Owned(screen, "weak");
        Assert.True(screen.TryGetHyperlink(token, out TerminalHyperlink? value));
        WeakReference weak = new(value);
        Assert.Equal(1, screen.CollectUnusedHyperlinks([], force: true));
        return (screen, weak);
    }

    private static int Owned(TerminalScreen screen, string uri)
        => screen.RegisterHyperlink(Encoding.UTF8.GetBytes(uri), [], 0, out _);
    private static string Open(string uri, string? id = null) => "\u001b]8;" + (id is null ? "" : "id=" + id) + ";" + uri + "\a";
    private const string Close = "\u001b]8;;\a";
    private static void Write(BasicVtProcessor processor, string text) => processor.Process(Encoding.UTF8.GetBytes(text));
    private static void Churn(BasicVtProcessor processor, int count)
    {
        StringBuilder input = new();
        for (int index = 0; index < count; index++) input.Append(Open("churn" + index)).Append(Close);
        Write(processor, input.ToString());
    }
    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }
}
