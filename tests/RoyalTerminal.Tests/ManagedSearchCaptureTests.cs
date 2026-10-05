// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSearchCaptureTests(ITestOutputHelper output)
{
    // Ghostty ActiveSearch copies only the active search window while retaining
    // history. WT checks buffer staleness and xterm.js invalidates line caches.
    // Here immutable row blocks retain history; storage/layout identities, not
    // renderer dirty flags, still detect every changed or shifted row.
    [Theory]
    [InlineData(1024)]
    [InlineData(16384)]
    public void ActiveRowEditsDoNotReallocateHistorySizedArraysAndIndexes(int rows)
    {
        TerminalScreen screen = Screen(rows);
        ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(screen, null);
        for (int i = 0; i < 32; i++) CaptureEdit(i);
        const int iterations = 32;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) CaptureEdit(i);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"rows={rows}, captures={iterations}, allocated={allocated}, ms={elapsed:F3}");
        Assert.InRange(allocated, 1, iterations * (4096L + ((rows + 63) / 64) * IntPtr.Size));
        Assert.Same(snapshot, ManagedSearchSnapshot.Capture(screen, snapshot));

        void CaptureEdit(int value)
        {
            screen.GetRow(rows - 1)[0] = new() { Codepoint = 'a' + value % 2, Width = 1 };
            snapshot = ManagedSearchSnapshot.Capture(screen, snapshot);
        }
    }

    [Fact]
    public void ChangedRowAndLayoutNeverMutateRetainedSnapshots()
    {
        TerminalScreen screen = Screen(130);
        screen.GetRow(64)[0] = new() { Codepoint = 'a', Width = 1 };
        ManagedSearchSnapshot first = ManagedSearchSnapshot.Capture(screen, null);
        screen.GetRow(64)[0] = new() { Codepoint = 'b', Width = 1 };
        ManagedSearchSnapshot second = ManagedSearchSnapshot.Capture(screen, first);
        Assert.NotSame(first.Rows[64], second.Rows[64]);
        Assert.Same(first.Rows[0], second.Rows[0]);
        Assert.Equal((int)'a', first.Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'b', second.Rows[64].ReadOnlyCells[0].Codepoint);
        screen.GetRow(64).WrapsToNext = true;
        ManagedSearchSnapshot wrapped = ManagedSearchSnapshot.Capture(screen, second);
        Assert.True(wrapped.Rows[64].WrapsToNext);
        Assert.False(second.Rows[64].WrapsToNext);
        Assert.Same(wrapped.Rows[64].SearchStorageIdentity, second.Rows[64].SearchStorageIdentity);
        screen.GetRow(64).WrapsToNext = false;
        screen.GetRow(64).IsWrapContinuation = true;
        ManagedSearchSnapshot continuation = ManagedSearchSnapshot.Capture(screen, wrapped);
        Assert.False(continuation.Rows[64].WrapsToNext);
        Assert.True(continuation.Rows[64].IsWrapContinuation);
        Assert.False(second.Rows[64].IsWrapContinuation);
    }

    [Fact]
    public void EvictionReusesMovedFrozenRowsAndPreservesOldSnapshots()
    {
        TerminalScreen screen = Screen(130);
        screen.GetRow(0)[0] = new() { Codepoint = 'a', Width = 1 };
        ManagedSearchSnapshot previous = ManagedSearchSnapshot.Capture(screen, null);
        for (int step = 0; step < 70; step++)
        {
            screen.AddRow()[0] = new() { Codepoint = 'b', Width = 1 };
            ManagedSearchSnapshot next = ManagedSearchSnapshot.Capture(screen, previous);
            Assert.Same(previous.Rows[1], next.Rows[0]);
            Assert.Same(previous.Rows[65], next.Rows[64]);
            Assert.Equal((int)'b', next.Rows[^1].ReadOnlyCells[0].Codepoint);
            previous = next;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(130)]
    public void SlicesPreserveExactRowsAcrossBlockBoundaries(int start)
    {
        TerminalScreen screen = Screen(130);
        ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(screen, null);
        ManagedSearchSnapshot slice = snapshot.Slice(start);
        Assert.Equal(130 - start, slice.Rows.Length);
        for (int i = 0; i < slice.Rows.Length; i++) Assert.Same(snapshot.Rows[i + start], slice.Rows[i]);
        ManagedSearchSnapshot nested = slice.Slice(slice.Rows.Length / 2);
        for (int i = 0; i < nested.Rows.Length; i++)
            Assert.Same(snapshot.Rows[start + slice.Rows.Length / 2 + i], nested.Rows[i]);
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.Slice(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.Slice(slice.Rows.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.Rows[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.Rows[slice.Rows.Length]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(129)]
    public void CommonPrefixStopsAtExactMutationEvenForMisalignedSlices(int changed)
    {
        TerminalScreen screen = Screen(130);
        ManagedSearchSnapshot first = ManagedSearchSnapshot.Capture(screen, null);
        screen.GetRow(changed)[0] = new() { Codepoint = 'x', Width = 1 };
        screen.GetRow(changed).IsDirty = false;
        ManagedSearchSnapshot second = ManagedSearchSnapshot.Capture(screen, first);
        Assert.Equal(changed, second.CommonPrefix(first));
        Assert.Equal(changed == 0 ? 129 : changed - 1, second.Slice(1).CommonPrefix(first.Slice(1)));
        Assert.Equal(130, first.CommonPrefix(first));
    }

    [Fact]
    public void UnchangedCaptureDoesNotAllocate()
    {
        TerminalScreen screen = Screen(130);
        ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(screen, null);
        for (int i = 0; i < 16; i++) snapshot = ManagedSearchSnapshot.Capture(screen, snapshot);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) snapshot = ManagedSearchSnapshot.Capture(screen, snapshot);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        GC.KeepAlive(snapshot);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(63, true)]
    [InlineData(64, false)]
    [InlineData(65, false)]
    [InlineData(128, false)]
    [InlineData(256, false)]
    public void SlicesRetainOnlyIntersectingBlocksNotExcludedHistory(int start, bool boundaryRetained)
    {
        (ManagedSearchSnapshot slice, WeakReference<object> first) = SliceAndReleaseHistory(start);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.Equal(boundaryRetained, first.TryGetTarget(out _));
        GC.KeepAlive(slice);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ManagedSearchSnapshot, WeakReference<object>) SliceAndReleaseHistory(int start)
    {
        ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(Screen(256), null);
        WeakReference<object> first = new(snapshot.Rows[0].SearchStorageIdentity);
        return (snapshot.Slice(start), first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedWeakCacheDoesNotRootReplacedRows(bool layoutOnly)
    {
        (ManagedSearchSnapshot current, WeakReference<TerminalRow> obsolete) = ReplaceRow(layoutOnly);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.False(obsolete.TryGetTarget(out _));
        GC.KeepAlive(current);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ManagedSearchSnapshot, WeakReference<TerminalRow>) ReplaceRow(bool layoutOnly)
    {
        TerminalScreen screen = Screen(130);
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        WeakReference<TerminalRow> obsolete = new(before.Rows[64]);
        if (layoutOnly) screen.GetRow(64).WrapsToNext = true;
        else screen.GetRow(64)[0] = new() { Codepoint = 'x', Width = 1 };
        return (ManagedSearchSnapshot.Capture(screen, before), obsolete);
    }

    [Fact]
    public async Task SharedCacheSupportsIndependentScreenCapturesWithoutCrossPublication()
    {
        TerminalScreen original = Screen(130);
        ManagedSearchSnapshot first = ManagedSearchSnapshot.Capture(original, null);
        TerminalScreen left = original.CreateStateCopy(), right = original.CreateStateCopy();
        await Task.WhenAll(Task.Run(() => Capture(left, 'a')), Task.Run(() => Capture(right, 'z')));
        Assert.False(first.Rows[64].ReadOnlyCells[0].HasContent);

        void Capture(TerminalScreen screen, char value)
        {
            ManagedSearchSnapshot snapshot = first;
            for (int i = 0; i < 100; i++)
            {
                lock (screen.SyncRoot)
                {
                    screen.GetRow(64)[0] = new() { Codepoint = value, Width = 1 };
                    screen.GetRow(64).WrapsToNext = i % 2 == 0;
                    snapshot = ManagedSearchSnapshot.Capture(screen, snapshot);
                }
                Assert.Equal((int)value, snapshot.Rows[64].ReadOnlyCells[0].Codepoint);
                Assert.Equal(i % 2 == 0, snapshot.Rows[64].WrapsToNext);
                Assert.Same(first.Rows[0], snapshot.Rows[0]);
            }
        }
    }

    private static TerminalScreen Screen(int rows)
    {
        TerminalScreen screen = new(8, 4, rows - 4);
        while (screen.TotalRows < rows) screen.AddRow();
        return screen;
    }
}
