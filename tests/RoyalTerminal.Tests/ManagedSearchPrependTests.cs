// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #14123 resumes exhausted history searches after incremental restore.
// WT Search::IsStale uses mutation IDs; xterm SearchLineCache clears on LF,
// cursor movement and resize. Royal keeps its COW identity invalidation and
// only reuses a shifted tail after full KMP/coordinate/format-state convergence.
public sealed class ManagedSearchPrependTests
{
    public static IEnumerable<object[]> NeedlesAndOffsets()
    {
        foreach (string needle in new[] { "needle", "aaa", "suffix\nneedle", "界e\u0301", "🦊", " ", "absent", "NEEDLE aaa" })
        foreach (int added in new[] { 1, 63, 129 }) yield return [needle, added];
    }

    [Theory]
    [MemberData(nameof(NeedlesAndOffsets))]
    public void PrependReusesCompletedTailWithExactCoordinates(string needle, int added)
    {
        TerminalScreen screen = Filled();
        ManagedSearchSnapshot full = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        search.Populate(full.Slice(added), needle, actual);
        search.Populate(full, needle, actual);
        AssertFresh(full, needle, actual);
        Assert.InRange(search.RowsScannedLastSearch, added, added + 128);
        search.Populate(full, needle, actual);
        Assert.Equal(0, search.RowsScannedLastSearch);
        AssertFresh(full, needle, actual);
    }

    [Theory]
    [InlineData("soft", "aaa")]
    [InlineData("soft", "aabaaa")]
    [InlineData("delayed", "a\n\nb")]
    [InlineData("empty", "\n")]
    public void SeamFormattingAndOverlappingPrefixesMatchACompleteScan(string kind, string needle)
    {
        TerminalScreen screen = new(8, 4, 2048);
        while (screen.TotalRows < 1024) screen.AddRow();
        for (int row = 0; row < screen.TotalRows; row++)
        {
            TerminalRow value = screen.GetRow(row);
            if (kind == "soft")
            {
                value.WrapsToNext = row != screen.TotalRows - 1;
                value.IsWrapContinuation = row != 0;
                for (int col = 0; col < 8; col++) value[col] = new() { Codepoint = col == 2 ? 'b' : 'a', Width = 1 };
            }
            else if (kind == "delayed" && row % 200 == 0)
                value[0] = new() { Codepoint = row % 400 == 0 ? 'a' : 'b', Width = 1 };
        }
        ManagedSearchSnapshot full = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        search.Populate(full.Slice(17), needle, actual);
        search.Populate(full, needle, actual);
        AssertFresh(full, needle, actual);
        if (kind == "soft") Assert.InRange(search.RowsScannedLastSearch, 17, 145);
        if (kind == "empty") Assert.Equal(full.Rows.Length, search.RowsScannedLastSearch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(900)]
    public void ChangedSuffixDoesNotReuseStaleMatches(int changedRow)
    {
        TerminalScreen screen = Filled();
        ManagedSearchSnapshot before = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        search.Populate(before.Slice(32), "needle", actual);
        screen.GetRow(32 + changedRow)[0].Codepoint = 'x';
        ManagedSearchSnapshot after = ManagedSearchSnapshot.Capture(screen, before);
        search.Populate(after, "needle", actual);
        AssertFresh(after, "needle", actual);
        Assert.Equal(after.Rows.Length, search.RowsScannedLastSearch);
        // The old capture remains independently searchable after COW mutation.
        search.Populate(before.Slice(32), "needle", actual);
        AssertFresh(before.Slice(32), "needle", actual);
    }

    [Fact]
    public void RepeatedPrependsRetainCheckpointsForSubsequentTailEditsAndReset()
    {
        TerminalScreen screen = Filled();
        ManagedSearchSnapshot full = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        int previous = 256;
        search.Populate(full.Slice(previous), "suffix\nneedle", actual);
        foreach (int start in new[] { 200, 127, 63, 0 })
        {
            ManagedSearchSnapshot snapshot = start == 0 ? full : full.Slice(start);
            search.Populate(snapshot, "suffix\nneedle", actual);
            AssertFresh(snapshot, "suffix\nneedle", actual);
            Assert.InRange(search.RowsScannedLastSearch, previous - start, previous - start + 128);
            previous = start;
        }
        screen.GetRow(screen.TotalRows - 2)[0].Codepoint = 'x';
        ManagedSearchSnapshot edited = ManagedSearchSnapshot.Capture(screen, full);
        search.Populate(edited, "suffix\nneedle", actual);
        AssertFresh(edited, "suffix\nneedle", actual);
        Assert.InRange(search.RowsScannedLastSearch, 1, 128);
        search.Reset();
        search.Populate(full, "aaa", actual);
        AssertFresh(full, "aaa", actual);
        Assert.Equal(full.Rows.Length, search.RowsScannedLastSearch);
    }

    [Fact]
    public void CancelledPrependDoesNotPublishOrDamageTheCompletedCache()
    {
        ManagedSearchSnapshot full = ManagedSearchSnapshot.Capture(Filled(), null);
        ManagedSearchSnapshot old = full.Slice(17);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        search.Populate(old, "needle", actual);
        Assert.Throws<OperationCanceledException>(() => search.Populate(full, "needle", actual, new(true)));
        Assert.Empty(actual);
        search.Populate(old, "needle", actual);
        Assert.Equal(0, search.RowsScannedLastSearch);
        AssertFresh(old, "needle", actual);
        search.Populate(full, "needle", actual);
        AssertFresh(full, "needle", actual);
    }

    [Fact]
    public void LargeSelfOverlappingNeedleKeepsBoundedCheckpointFallbackCorrect()
    {
        TerminalScreen screen = new(8, 4, 2048);
        using (BasicVtProcessor writer = new(screen)) writer.Process(Encoding.ASCII.GetBytes(new string('a', 8192)));
        ManagedSearchSnapshot full = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        string needle = new('a', 300);
        search.Populate(full.Slice(17), needle, actual);
        search.Populate(full, needle, actual);
        AssertFresh(full, needle, actual);
    }

    [Fact]
    public void IncrementalSnapshotHistoryRestoreResumesCompletedSearch()
    {
        TerminalScreen source = Filled();
        using BasicVtProcessor writer = new(source);
        using GhosttySnapshotStateReader reader = new(writer.GetBinarySnapshot(), new());
        TerminalScreen screen = GhosttySnapshotLiveScreen.Stage(reader.ReadReady(), TerminalTheme.Dark, 4096);
        ManagedSearchSnapshot capture = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> actual = [];
        search.Populate(capture, "suffix\nneedle", actual);
        bool reused = false;
        while (reader.ReadNextHistoryPage() is { } history)
        {
            int added = screen.PrependSnapshotHistory(history.Key, history.Page);
            capture = ManagedSearchSnapshot.Capture(screen, capture);
            search.Populate(capture, "suffix\nneedle", actual);
            AssertFresh(capture, "suffix\nneedle", actual);
            reused |= search.RowsScannedLastSearch < capture.Rows.Length && capture.Rows.Length > added + 128;
        }
        Assert.True(reused);
    }

    private static TerminalScreen Filled()
    {
        TerminalScreen screen = new(32, 4, 4096);
        using BasicVtProcessor writer = new(screen);
        byte[] line = Encoding.UTF8.GetBytes("needle aaa 界e\u0301 🦊 suffix\r\n");
        for (int row = 0; row < 1024; row++) writer.Process(line);
        return screen;
    }

    private static void AssertFresh(ManagedSearchSnapshot snapshot, string needle, List<TerminalSearchMatch> actual)
    {
        List<TerminalSearchMatch> expected = [];
        new ManagedTerminalSearch().Populate(snapshot, needle, expected);
        Assert.Equal(expected, actual);
    }
}
