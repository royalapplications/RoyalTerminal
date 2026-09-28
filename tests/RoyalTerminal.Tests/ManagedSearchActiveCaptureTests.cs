// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty ActiveSearch.update copies the mutable tail and its wrapped overlap.
// Managed captures retain all immutable history blocks and compare only the
// active window plus its boundary block when row observation proves that safe.
// Direct history edits remain supported, unlike assuming history is immutable.
public sealed class ManagedSearchActiveCaptureTests
{
    [Theory]
    [InlineData(63, 4, 0)]
    [InlineData(64, 4, 3)]
    [InlineData(129, 4, 0)]
    [InlineData(130, 4, 3)]
    [InlineData(256, 65, 0)]
    [InlineData(16384, 24, 23)]
    public void ActiveChangesReuseHistoryAcrossAlignedAndPartialBlocks(int count, int viewport, int activeRow)
    {
        TerminalScreen screen = Screen(count, viewport);
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong revision = changes.Revision;
        int index = count - viewport + activeRow;
        screen.GetRow(index)[0] = new() { Codepoint = 'x', Width = 1 };
        Assert.Equal(count - viewport, changes.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot after = ManagedSearchSnapshot.Capture(screen, before);
        Assert.Equal(index, after.CommonPrefix(before));
        Assert.Equal((int)'x', after.Rows[index].ReadOnlyCells[0].Codepoint);
        Assert.False(before.Rows[index].ReadOnlyCells[0].HasContent);
        for (int i = 0; i < count; i++)
            if (i != index) Assert.Same(before.Rows[i], after.Rows[i]);
        Assert.Same(after, ManagedSearchSnapshot.Capture(screen, after));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(125)]
    public void AcknowledgedHistoryEditAllowsTheNextActiveOnlyCapture(int index)
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong revision = changes.Revision;
        screen.GetRow(index)[0] = new() { Codepoint = 'h', Width = 1 };
        Assert.Equal(0, changes.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot history = ManagedSearchSnapshot.Capture(screen, before);
        Assert.Equal((int)'h', history.Rows[index].ReadOnlyCells[0].Codepoint);
        revision = changes.Revision;
        screen.GetRow(129)[0] = new() { Codepoint = 'a', Width = 1 };
        Assert.Equal(126, changes.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot active = ManagedSearchSnapshot.Capture(screen, history);
        Assert.Same(history.Rows[index], active.Rows[index]);
        Assert.Equal((int)'a', active.Rows[129].ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryInvalidationCannotBeHiddenByAnActiveEdit(bool historyFirst)
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong revision = changes.Revision;
        int first = historyFirst ? 0 : 129, second = historyFirst ? 129 : 0;
        screen.GetRow(first)[0] = new() { Codepoint = 'x', Width = 1 };
        screen.GetRow(second)[0] = new() { Codepoint = 'y', Width = 1 };
        Assert.Equal(0, changes.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot after = ManagedSearchSnapshot.Capture(screen, before);
        Assert.Equal((int)'x', after.Rows[first].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'y', after.Rows[second].ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralRebindingReclassifiesRecycledRows(bool moveIntoHistory)
    {
        TerminalScreen screen = Screen();
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken old = rows.GetSearchChangeToken();
        ulong revision = old.Revision;
        TerminalRow recycled = rows[moveIntoHistory ? 129 : 0];
        if (moveIntoHistory)
        {
            rows.RemoveRange(129, 1);
            rows.PrependRange([recycled]);
        }
        else
        {
            rows.RemoveFirst();
            rows.Add(recycled);
        }
        Assert.True(old.RequiresRebinding);
        Assert.Equal(0, old.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot shifted = ManagedSearchSnapshot.Capture(screen, before);
        TerminalSearchChangeToken changes = rows.GetSearchChangeToken();
        Assert.NotSame(old, changes);
        Assert.False(changes.RequiresRebinding);
        revision = changes.Revision;
        recycled[0] = new() { Codepoint = 'r', Width = 1 };
        Assert.Equal(moveIntoHistory ? 0 : 126, changes.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot edited = ManagedSearchSnapshot.Capture(screen, shifted);
        Assert.Equal((int)'r', edited.Rows[moveIntoHistory ? 0 : 129].ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void AWrapperInBothHistoryAndActiveRowsMustInvalidateHistory()
    {
        TerminalScreen screen = Screen();
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        TerminalRow shared = rows[0];
        rows[129] = shared;
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = rows.GetSearchChangeToken();
        ulong revision = changes.Revision;
        shared[0] = new() { Codepoint = 'x', Width = 1 };
        Assert.Equal(0, changes.UnchangedHistoryPrefix(revision));
        ManagedSearchSnapshot both = ManagedSearchSnapshot.Capture(screen, before);
        Assert.Equal((int)'x', both.Rows[0].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'x', both.Rows[129].ReadOnlyCells[0].Codepoint);
        rows[0] = new(8);
        ManagedSearchSnapshot activeOnly = ManagedSearchSnapshot.Capture(screen, both);
        changes = rows.GetSearchChangeToken();
        revision = changes.Revision;
        shared[0] = new() { Codepoint = 'y', Width = 1 };
        Assert.Equal(126, changes.UnchangedHistoryPrefix(revision));
        Assert.Equal((int)'y', ManagedSearchSnapshot.Capture(screen, activeOnly).Rows[129].ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservationTransferInvalidatesHistoryRegardlessOfTheNewOwnersWindow(bool leftHistory)
    {
        TerminalScreen left = Screen(), right = Screen();
        int leftIndex = leftHistory ? 0 : 129, rightIndex = leftHistory ? 129 : 0;
        TerminalRow shared = left.GetRow(leftIndex);
        ManagedSearchSnapshot a = ManagedSearchSnapshot.Capture(left, null);
        TerminalSearchChangeToken leftChanges = left.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong leftRevision = leftChanges.Revision;
        right.GetSnapshotRows(0)![rightIndex] = shared;
        ManagedSearchSnapshot b = ManagedSearchSnapshot.Capture(right, null);
        Assert.Equal(0, leftChanges.UnchangedHistoryPrefix(leftRevision));
        shared[0] = new() { Codepoint = 'x', Width = 1 };
        ManagedSearchSnapshot leftChanged = ManagedSearchSnapshot.Capture(left, a);
        ManagedSearchSnapshot rightChanged = ManagedSearchSnapshot.Capture(right, b);
        Assert.Equal((int)'x', leftChanged.Rows[leftIndex].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'x', rightChanged.Rows[rightIndex].ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void AnOlderConsumerCannotMissChangesAcknowledgedByAnotherCapture()
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot oldest = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong oldestRevision = changes.Revision;
        screen.GetRow(0)[0] = new() { Codepoint = 'h', Width = 1 };
        ManagedSearchSnapshot newer = ManagedSearchSnapshot.Capture(screen, oldest);
        ulong newerRevision = changes.Revision;
        screen.GetRow(129)[0] = new() { Codepoint = 'a', Width = 1 };
        Assert.Equal(0, changes.UnchangedHistoryPrefix(oldestRevision));
        Assert.Equal(126, changes.UnchangedHistoryPrefix(newerRevision));
        ManagedSearchSnapshot fromOld = ManagedSearchSnapshot.Capture(screen, oldest);
        Assert.Equal((int)'h', fromOld.Rows[0].ReadOnlyCells[0].Codepoint);
        Assert.Equal((int)'a', fromOld.Rows[129].ReadOnlyCells[0].Codepoint);
        Assert.Equal(0, changes.UnchangedHistoryPrefix(newerRevision));
        ManagedSearchSnapshot fromNew = ManagedSearchSnapshot.Capture(screen, newer);
        Assert.Same(newer.Rows[0], fromNew.Rows[0]);
        Assert.Equal((int)'a', fromNew.Rows[129].ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void GrowingTheActiveWindowRebindsItsHistoryBoundary()
    {
        TerminalScreen screen = Screen();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        TerminalSearchChangeToken old = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        screen.Resize(8, 5);
        ManagedSearchSnapshot larger = ManagedSearchSnapshot.Capture(screen, before);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        Assert.NotSame(old, changes);
        Assert.Equal(125, changes.ActiveStart);
        ulong revision = changes.Revision;
        screen.GetRow(125)[0] = new() { Codepoint = 'a', Width = 1 };
        Assert.Equal(125, changes.UnchangedHistoryPrefix(revision));
        Assert.Equal((int)'a', ManagedSearchSnapshot.Capture(screen, larger).Rows[125].ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchesAcrossTheHistoryBoundaryRemainEquivalentToFreshSearch(bool wrap)
    {
        TerminalScreen screen = Screen();
        TerminalRow history = screen.GetRow(125);
        const string text = "abc12345";
        for (int i = 0; i < text.Length; i++) history[i] = new() { Codepoint = text[i], Width = 1 };
        history.WrapsToNext = wrap;
        screen.GetRow(126)[0] = new() { Codepoint = 'x', Width = 1 };
        string needle = wrap ? "456" : "45\n6";
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [], expected = [];
        search.Populate(screen, needle, actual);
        Assert.Empty(actual);
        TerminalSearchChangeToken changes = screen.GetSnapshotRows(0)!.GetSearchChangeToken();
        ulong revision = changes.Revision;
        screen.GetRow(126)[0] = new() { Codepoint = '6', Width = 1 };
        Assert.Equal(126, changes.UnchangedHistoryPrefix(revision));
        search.Populate(screen, needle, actual);
        new ManagedTerminalSearch().Populate(screen, needle, expected);
        Assert.Single(actual);
        Assert.Equal(expected, actual);
        history[7] = new() { Codepoint = 'q', Width = 1 };
        search.Populate(screen, needle, actual);
        Assert.Empty(actual);
    }

    [Fact]
    public void PendingHistoryInvalidationAndSaturationNeverAllowPrefixReuse()
    {
        TerminalSearchChangeToken changes = new(activeStart: 126);
        Assert.Equal(0, changes.UnchangedHistoryPrefix(0));
        changes.RecordCaptured();
        changes.Invalidate(history: false);
        Assert.Equal(126, changes.UnchangedHistoryPrefix(0));
        changes.Invalidate();
        changes.Invalidate(history: false);
        Assert.Equal(0, changes.UnchangedHistoryPrefix(0));
        Assert.Equal(0, changes.UnchangedHistoryPrefix(0)); // No success acknowledgment yet.
        changes.RecordCaptured();
        Assert.Equal(126, changes.UnchangedHistoryPrefix(changes.Revision));
        changes.InvalidateStructure();
        Assert.Equal(0, changes.UnchangedHistoryPrefix(changes.Revision - 1));
        TerminalSearchChangeToken saturated = new(ulong.MaxValue - 1, 126);
        saturated.RecordCaptured();
        saturated.Invalidate(history: false);
        Assert.Equal(0, saturated.UnchangedHistoryPrefix(ulong.MaxValue - 1));
        saturated.RecordCaptured();
        Assert.Equal(0, saturated.UnchangedHistoryPrefix(ulong.MaxValue));
    }

    private static TerminalScreen Screen(int count = 130, int viewport = 4)
    {
        TerminalScreen screen = new(8, viewport, count - viewport);
        while (screen.TotalRows < count) screen.AddRow();
        return screen;
    }
}
