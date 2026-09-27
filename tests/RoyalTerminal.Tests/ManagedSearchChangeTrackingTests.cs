// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty gates active search on content dirtiness and screen generations; WT
// has a buffer mutation ID; xterm.js invalidates its line cache on buffer events.
// The managed gate watches low-level rows too, because hosts can edit them
// directly. Changed captures still compare exact COW storage/layout identities.
public sealed class ManagedSearchChangeTrackingTests
{
    [Theory]
    [InlineData("indexer")]
    [InlineData("span")]
    [InlineData("shrink")]
    [InlineData("grow")]
    [InlineData("clear")]
    [InlineData("copy")]
    [InlineData("copy-active")]
    [InlineData("clear-preserved")]
    [InlineData("swap")]
    [InlineData("wrap")]
    [InlineData("continuation")]
    [InlineData("colors")]
    public void EveryRowMutationInvalidatesCaptureWithoutChangingRetainedCells(string mutation)
    {
        TerminalScreen screen = Screen();
        TerminalRow row = screen.GetRow(64);
        row[0] = new() { Codepoint = 'a', Width = 1 };
        TerminalRow source = new(8);
        source[0] = new() { Codepoint = 'z', Width = 1 };
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong revision = changes.Revision;

        switch (mutation)
        {
            case "indexer": row[0] = source.ReadOnlyCells[0]; break;
            case "span": row.Cells[0] = source.ReadOnlyCells[0]; break;
            case "shrink": row.Resize(4); break;
            case "grow": row.Resize(12); break;
            case "clear": row.Clear(); break;
            case "copy": row.CopyFrom(source); break;
            case "copy-active": row.CopyActiveFrom(source); break;
            case "clear-preserved": row.ClearPreservedCellsFrom(0); break;
            case "swap": row.SwapActiveStorage(source); break;
            case "wrap": row.WrapsToNext = true; break;
            case "continuation": row.IsWrapContinuation = true; break;
            case "colors": Assert.True(row.ResolveCellColors(TerminalTheme.Dark)); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        row.IsDirty = false;
        Assert.False(changes.Matches(revision));
        ManagedSearchSnapshot after = ManagedSearchSnapshot.Capture(screen, before);
        Assert.NotSame(before, after);
        Assert.NotSame(before.Rows[64], after.Rows[64]);
        Assert.Same(before.Rows[0], after.Rows[0]);
        Assert.True(row.HasSameSearchContent(after.Rows[64]));
        Assert.Equal((int)'a', before.Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.Equal(8, before.Rows[64].Columns);
        Assert.False(before.Rows[64].WrapsToNext);
        Assert.False(before.Rows[64].IsWrapContinuation);
        Assert.Same(after, ManagedSearchSnapshot.Capture(screen, after));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("add-range")]
    [InlineData("prepend")]
    [InlineData("replace")]
    [InlineData("remove-first")]
    [InlineData("remove-middle")]
    [InlineData("remove-tail")]
    [InlineData("clear")]
    [InlineData("ring-wrap")]
    public void StructuralChangesInvalidateEvenWhenTheRowCountStaysTheSame(string mutation)
    {
        TerminalScreen screen = Screen();
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = rows.GetSearchChangeToken();
        ulong revision = changes.Revision;
        TerminalRow added = new(8);
        switch (mutation)
        {
            case "add": rows.Add(added); break;
            case "add-range": rows.AddRange([added, new(8)]); break;
            case "prepend": rows.PrependRange([added]); break;
            case "replace": rows[64] = added; break;
            case "remove-first": rows.RemoveFirst(); break;
            case "remove-middle": rows.RemoveRange(63, 2); break;
            case "remove-tail": rows.RemoveRange(rows.Count - 1, 1); break;
            case "clear": rows.Clear(); break;
            case "ring-wrap":
                for (int i = 0; i < 260; i++) { rows.RemoveFirst(); rows.Add(new(8)); }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.False(changes.Matches(revision));
        ManagedSearchSnapshot after = ManagedSearchSnapshot.Capture(screen, before);
        Assert.NotSame(before, after);
        Assert.Equal(rows.Count, after.Rows.Length);
        for (int i = 0; i < rows.Count; i++) Assert.True(rows[i].HasSameSearchContent(after.Rows[i]));
        Assert.Equal(130, before.Rows.Length);
        Assert.Same(after, ManagedSearchSnapshot.Capture(screen, after));
        if (mutation is "add" or "add-range" or "prepend" or "replace")
        {
            revision = changes.Revision;
            added[0] = new() { Codepoint = 'x', Width = 1 };
            Assert.False(changes.Matches(revision));
            Assert.NotSame(after, ManagedSearchSnapshot.Capture(screen, after));
        }
    }

    [Fact]
    public void SharedWrappersReacquireObservationEvenWhenAllBlocksRemainUnchanged()
    {
        TerminalScreen left = Screen(), right = Screen();
        TerminalRow shared = left.GetRow(64);
        ManagedSearchSnapshot leftCapture = ManagedSearchSnapshot.Capture(left, null);
        right.GetSnapshotRows(0)![64] = shared;
        ManagedSearchSnapshot rightCapture = ManagedSearchSnapshot.Capture(right, null);
        for (int i = 0; i < 4; i++)
        {
            // Full comparison must reattach rows even in a wholly reused block.
            ManagedSearchSnapshot reacquired = ManagedSearchSnapshot.Capture(left, leftCapture);
            Assert.Same(leftCapture.Rows, reacquired.Rows);
            leftCapture = reacquired;
            rightCapture = ManagedSearchSnapshot.Capture(right, rightCapture);
            shared[0] = new() { Codepoint = 'a' + i, Width = 1 };
            ManagedSearchSnapshot leftChanged = ManagedSearchSnapshot.Capture(left, leftCapture);
            ManagedSearchSnapshot rightChanged = ManagedSearchSnapshot.Capture(right, rightCapture);
            Assert.NotSame(leftCapture.Rows[64], leftChanged.Rows[64]);
            Assert.NotSame(rightCapture.Rows[64], rightChanged.Rows[64]);
            Assert.Equal('a' + i, leftChanged.Rows[64].ReadOnlyCells[0].Codepoint);
            Assert.Equal('a' + i, rightChanged.Rows[64].ReadOnlyCells[0].Codepoint);
            leftCapture = leftChanged;
            rightCapture = rightChanged;
        }
    }

    [Fact]
    public void StateCopiesHaveIndependentObserversAndAdoptionUsesTheTransferredBuffer()
    {
        TerminalScreen original = Screen();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(original, null);
        TerminalScreen copy = original.CreateStateCopy();
        ManagedSearchSnapshot copyBefore = ManagedSearchSnapshot.Capture(copy, before);
        Assert.Same(before.Rows, copyBefore.Rows);
        copy.GetRow(64)[0] = new() { Codepoint = 'x', Width = 1 };
        ManagedSearchSnapshot copyAfter = ManagedSearchSnapshot.Capture(copy, copyBefore);
        Assert.Same(before, ManagedSearchSnapshot.Capture(original, before));
        Assert.False(before.Rows[64].ReadOnlyCells[0].HasContent);
        Assert.Equal((int)'x', copyAfter.Rows[64].ReadOnlyCells[0].Codepoint);
        original.AdoptStateFrom(copy);
        Assert.Same(copyAfter, ManagedSearchSnapshot.Capture(original, copyAfter));
        original.GetRow(64).WrapsToNext = true;
        Assert.True(ManagedSearchSnapshot.Capture(original, copyAfter).Rows[64].WrapsToNext);
        Assert.False(copyAfter.Rows[64].WrapsToNext);
    }

    [Fact]
    public void IndependentConsumersCannotAdvanceEachOthersImmutableCaptureStamp()
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot first = ManagedSearchSnapshot.Capture(screen, null);
        screen.GetRow(64)[0] = new() { Codepoint = 'a', Width = 1 };
        ManagedSearchSnapshot second = ManagedSearchSnapshot.Capture(screen, first);
        screen.GetRow(64)[0] = new() { Codepoint = 'b', Width = 1 };
        ManagedSearchSnapshot third = ManagedSearchSnapshot.Capture(screen, second);
        ManagedSearchSnapshot fromFirst = ManagedSearchSnapshot.Capture(screen, first);
        Assert.Same(third, ManagedSearchSnapshot.Capture(screen, third));
        Assert.Equal((int)'b', fromFirst.Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'a', second.Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.False(first.Rows[64].ReadOnlyCells[0].HasContent);
    }

    [Fact]
    public void RendererAndNonSearchMetadataDoNotInvalidateIdleCapture()
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalRow row = screen.GetRow(64);
        row.IsDirty = false;
        row.SemanticPrompt = TerminalSemanticPrompt.Prompt;
        row.IsTransientResizeRow = true;
        row.MarkContentMutation();
        row.WrapsToNext = false;
        row.IsWrapContinuation = false;
        row.Resize(row.Columns);
        _ = row.ReadOnlyCells[0];
        _ = row.ReadOnlyPreservedCells[0];
        _ = row.CreateStateCopy();
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        rows.RemoveFirst(0);
        rows.RemoveRange(0, 0);
        rows.PrependRange([]);
        Assert.Same(before, ManagedSearchSnapshot.Capture(screen, before));
    }

    [Fact]
    public void RetiredRowInvalidationsAreConservativeAndDoNotRepeatAfterRecapture()
    {
        TerminalScreen screen = Screen();
        TerminalRow retired = screen.GetRow(64);
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        screen.GetSnapshotRows(0)![64] = new(8);
        ManagedSearchSnapshot current = ManagedSearchSnapshot.Capture(screen, before);
        retired.Clear();
        ManagedSearchSnapshot refreshed = ManagedSearchSnapshot.Capture(screen, current);
        Assert.Same(current.Rows, refreshed.Rows);
        Assert.Same(refreshed, ManagedSearchSnapshot.Capture(screen, refreshed));
    }

    [Fact]
    public void StorageSwapNotifiesBothObservedOwners()
    {
        TerminalScreen left = Screen(), right = Screen();
        left.GetRow(64)[0] = new() { Codepoint = 'a', Width = 1 };
        right.GetRow(64)[0] = new() { Codepoint = 'b', Width = 1 };
        ManagedSearchSnapshot a = ManagedSearchSnapshot.Capture(left, null);
        ManagedSearchSnapshot b = ManagedSearchSnapshot.Capture(right, null);
        left.GetRow(64).SwapActiveStorage(right.GetRow(64));
        Assert.Equal((int)'b', ManagedSearchSnapshot.Capture(left, a).Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'a', ManagedSearchSnapshot.Capture(right, b).Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'a', a.Rows[64].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'b', b.Rows[64].ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void BufferSwitchAndViewportResizeDoNotReuseIncompatibleCaptures()
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot primary = ManagedSearchSnapshot.Capture(screen, null);
        screen.SwitchToAlternateBuffer(clear: false);
        ManagedSearchSnapshot alternate = ManagedSearchSnapshot.Capture(screen, primary);
        Assert.True(alternate.Alternate);
        Assert.NotSame(primary, alternate);
        screen.SwitchToPrimaryBuffer();
        Assert.Same(primary, ManagedSearchSnapshot.Capture(screen, primary));
        screen.Resize(8, 5);
        ManagedSearchSnapshot taller = ManagedSearchSnapshot.Capture(screen, primary);
        Assert.Equal(5, taller.ViewportRows);
        Assert.NotSame(primary, taller);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(130)]
    public void SlicesNeverQualifyForTheWholeBufferFastPath(int start)
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        ManagedSearchSnapshot slice = before.Slice(start);
        ManagedSearchSnapshot restored = ManagedSearchSnapshot.Capture(screen, slice);
        Assert.NotSame(slice, restored);
        Assert.Equal(130, restored.Rows.Length);
        for (int i = 0; i < 130; i++) Assert.True(screen.GetRow(i).HasSameSearchContent(restored.Rows[i]));
        Assert.Same(restored, ManagedSearchSnapshot.Capture(screen, restored));
    }

    [Fact]
    public void SaturationNeverMatchesAnOldOrNewStamp()
    {
        TerminalSearchChangeToken changes = new(ulong.MaxValue - 1);
        Assert.True(changes.Matches(ulong.MaxValue - 1));
        changes.Invalidate();
        changes.Invalidate();
        Assert.Equal(ulong.MaxValue, changes.Revision);
        Assert.False(changes.Matches(ulong.MaxValue - 1));
        Assert.False(changes.Matches(ulong.MaxValue));
        Assert.False(changes.Matches(0));
    }

    [Fact]
    public void CaptureTokenDoesNotRootLiveBufferOrRowWrappers()
    {
        (ManagedSearchSnapshot capture, WeakReference<TerminalRowBuffer> rows, WeakReference<TerminalRow> row) = CaptureAndRelease();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.False(rows.TryGetTarget(out _));
        Assert.False(row.TryGetTarget(out _));
        GC.KeepAlive(capture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ManagedSearchSnapshot, WeakReference<TerminalRowBuffer>, WeakReference<TerminalRow>) CaptureAndRelease()
    {
        TerminalScreen screen = Screen();
        return (ManagedSearchSnapshot.Capture(screen, null), new(screen.GetSnapshotRows(0)!), new(screen.GetRow(64)));
    }

    private static TerminalScreen Screen()
    {
        TerminalScreen screen = new(8, 4, 126);
        while (screen.TotalRows < 130) screen.AddRow();
        return screen;
    }
}
