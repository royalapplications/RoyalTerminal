// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPendingHyperlinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalUpdatesBothIdentityIndicesWithoutChangingACowRegistry(bool explicitId)
    {
        TerminalHyperlinkRegistry registry = new(), retained = new();
        byte[] id = explicitId ? "id"u8.ToArray() : [];
        TerminalHyperlink value = registry.Add(1, "u"u8, id, 7);
        retained.CopyFrom(registry);
        Assert.True(registry.RemovePending(1));
        Assert.False(registry.RemovePending(1));
        Assert.False(registry.TryGet(1, out _));
        Assert.False(registry.TryFind("u"u8, id, 7, out _));
        Assert.True(retained.TryGet(1, out TerminalHyperlink? original));
        Assert.Same(value, original);
        Assert.True(retained.TryFind("u"u8, id, 7, out int token));
        Assert.Equal(1, token);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CollisionRemovalKeepsOrderAndDoesNotMutateSharedNodes(int remove)
    {
        TerminalHyperlink value = new("u"u8, default, 0);
        TerminalHyperlinkRegistry.Entry? head = null;
        for (int i = 1; i <= 4; i++) head = new(i, value, head);
        TerminalHyperlinkRegistry.Entry? result = TerminalHyperlinkRegistry.RemoveEntry(head!, remove);
        for (int expected = 4; expected >= 1; expected--)
        {
            Assert.Equal(expected, head!.Token);
            head = head.Next;
            if (expected == remove) continue;
            Assert.Equal(expected, result!.Token);
            Assert.Same(value, result.Value);
            result = result.Next;
        }
        Assert.Null(head);
        Assert.Null(result);
    }

    [Fact]
    public void FailedDuplicateTokenInsertionCannotPublishASecondBucket()
    {
        TerminalHyperlinkRegistry registry = new();
        TerminalHyperlink original = registry.Add(1, "old"u8, default, 0);
        Assert.Throws<ArgumentException>(() => registry.Add(1, "new"u8, default, 1));
        Assert.True(registry.TryFind("old"u8, default, 0, out int token));
        Assert.Equal(1, token);
        Assert.False(registry.TryFind("new"u8, default, 1, out _));
        Assert.True(registry.TryGet(1, out TerminalHyperlink? actual));
        Assert.Same(original, actual);
    }

    [Fact]
    public void HeadRemovalAllocatesNothing()
    {
        TerminalHyperlinkRegistry[] registries = new TerminalHyperlinkRegistry[32];
        for (int i = 0; i < registries.Length; i++)
        {
            registries[i] = new();
            registries[i].Add(1, "u"u8, default, 0);
        }
        Assert.True(registries[0].RemovePending(1));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool removed = true;
        for (int i = 1; i < registries.Length; i++) removed &= registries[i].RemovePending(1);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(removed);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RollbackDoesNotRewindPastLaterRegistrationsOrChangeRetainedOwners()
    {
        TerminalScreen screen = new(8, 1);
        int first = screen.RegisterHyperlink("first"u8, default, 0);
        int pending = screen.RegisterHyperlink("pending"u8, default, 1, out bool created);
        int later = screen.RegisterHyperlink("later"u8, default, 2);
        TerminalScreen retained = screen.CreateStateCopy();
        Assert.True(created);
        screen.DiscardPendingHyperlink(pending);
        Assert.False(screen.TryGetHyperlink(pending, out _));
        Assert.False(screen.TryGetHyperlinkUrl(pending, out _));
        Assert.True(screen.TryGetHyperlink(first, out _));
        Assert.True(screen.TryGetHyperlink(later, out _));
        Assert.True(retained.TryGetHyperlink(pending, out _));
        Assert.True(retained.TryGetHyperlinkUrl(pending, out _));
        Assert.Equal(later + 1, screen.RegisterHyperlink("next"u8, default, 3));
    }

    [Fact]
    public void DiscardedIdentityAndItsEncodedBytesAreNotRootedByALiveScreen()
    {
        (TerminalScreen screen, WeakReference<TerminalHyperlink> weak) = Discarded();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.False(weak.TryGetTarget(out _));
        GC.KeepAlive(screen);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TerminalScreen, WeakReference<TerminalHyperlink>) Discarded()
    {
        TerminalScreen screen = new(8, 1);
        int token = screen.RegisterHyperlink(new byte[8192], "id"u8, 0, out bool created);
        Assert.True(created);
        Assert.True(screen.TryGetHyperlink(token, out TerminalHyperlink? value));
        _ = value!.SnapshotEncoding;
        WeakReference<TerminalHyperlink> weak = new(value);
        screen.DiscardPendingHyperlink(token);
        return (screen, weak);
    }
}
