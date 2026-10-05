// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedIncrementalSearchTests
{
    // Ghostty separates mutable active rows from retained history and scans on
    // an owned worker. WT uses buffer-staleness checks, and xterm.js invalidates
    // its line cache on writes/resizes. Our COW identities detect both without
    // depending on dirty flags that a renderer may already have acknowledged.
    [Fact]
    public void UnchangedSnapshotUsesCacheAndActiveEditRetainsHistoryCheckpoints()
    {
        TerminalScreen screen = new(40, 4, 4000);
        using BasicVtProcessor processor = new(screen);
        for (int i = 0; i < 2000; i++) processor.Process("prefix needle suffix\r\n"u8);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> matches = new(2048);
        search.Populate(screen, "needle", matches);
        Assert.Equal(2000, matches.Count);
        search.Populate(screen, "needle", matches);
        Assert.Equal(0, search.RowsScannedLastSearch);
        processor.Process("new needle"u8);
        search.Populate(screen, "needle", matches);
        Assert.Equal(2001, matches.Count);
        Assert.InRange(search.RowsScannedLastSearch, 1, 70);
        screen.GetRow(0)[0] = TerminalCell.Empty();
        screen.GetRow(0).IsDirty = false;
        search.Populate(screen, "needle", matches);
        Assert.True(search.RowsScannedLastSearch >= 2000);
    }

    [Fact]
    public void SnapshotRowsRemainImmutableAfterSourceMutationAndEviction()
    {
        TerminalScreen screen = new(8, 2, 3);
        using BasicVtProcessor processor = new(screen);
        processor.Process("old\r\nrow"u8);
        ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(screen, null);
        processor.Process("\u001b[Hnew\r\n1\r\n2\r\n3\r\n4\r\n5"u8);
        Assert.Equal((int)'o', snapshot.Rows[0].ReadOnlyCells[0].Codepoint);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> matches = [];
        search.Populate(snapshot, "old", matches);
        Assert.Single(matches);
        search.Populate(screen, "old", matches);
        Assert.Empty(matches);
    }

    [Theory]
    [InlineData("aa")]
    [InlineData("aba")]
    [InlineData("needle")]
    public void CheckpointsRetainOverlappingKmpPrefixes(string needle)
    {
        TerminalScreen screen = new(40, 4, 1000);
        using BasicVtProcessor processor = new(screen);
        byte[] line = Encoding.UTF8.GetBytes(needle + "\r\n");
        for (int i = 0; i < 500; i++) processor.Process(line);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> matches = [];
        search.Populate(screen, needle, matches);
        processor.Process(line);
        search.Populate(screen, needle, matches);
        Assert.Equal(501, matches.Count);
        Assert.InRange(search.RowsScannedLastSearch, 1, 70);
    }

    [Theory]
    [InlineData("needle")]
    [InlineData("suffix\nnext")]
    [InlineData("a\n\nb")]
    [InlineData("abababa")]
    [InlineData("界e\u0301")]
    public void IncrementalResultsEqualFreshScanAcrossCheckpointBoundaries(string needle)
    {
        TerminalScreen screen = new(20, 4, 1000);
        using BasicVtProcessor processor = new(screen);
        for (int i = 0; i < 150; i++) processor.Process("needle abababa\r\n"u8);
        ManagedTerminalSearch cached = new();
        List<TerminalSearchMatch> actual = [];
        List<TerminalSearchMatch> expected = [];
        cached.Populate(screen, needle, actual);
        processor.Process(Encoding.UTF8.GetBytes("suffix\r\nnext a\r\n\r\nb 界e\u0301"));
        cached.Populate(screen, needle, actual);
        new ManagedTerminalSearch().Populate(screen, needle, expected);
        Assert.Equal(expected, actual);
        processor.ResizeScreen(7, 4, 70, 40, reflowOnResize: true);
        cached.Populate(screen, needle, actual);
        new ManagedTerminalSearch().Populate(screen, needle, expected);
        Assert.Equal(expected, actual);
        screen.ScrollbackLimit = 5;
        cached.Populate(screen, needle, actual);
        new ManagedTerminalSearch().Populate(screen, needle, expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CancelledScanCanResumeWithoutPublishingIncompleteResults()
    {
        TerminalScreen screen = new(40, 4, 1000);
        using BasicVtProcessor processor = new(screen);
        for (int i = 0; i < 500; i++) processor.Process("needle\r\n"u8);
        ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(screen, null);
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> matches = [];
        Assert.Throws<OperationCanceledException>(() => search.Populate(snapshot, "needle", matches, new(true)));
        Assert.Empty(matches);
        search.Populate(snapshot, "needle", matches);
        Assert.Equal(500, matches.Count);
        search.Reset();
        search.Populate(snapshot, "needle", matches);
        Assert.Equal(500, matches.Count);
        Assert.True(search.RowsScannedLastSearch >= 500);
    }

    [Fact]
    public async Task WorkerDiscardsOldNeedlesAndEditedSnapshotResults()
    {
        TerminalScreen screen = new(40, 4, 4000);
        using BasicVtProcessor processor = new(screen);
        for (int i = 0; i < 2000; i++) processor.Process("old needle\r\n"u8);
        List<TerminalSearchMatch> matches = [];
        Assert.Equal(TerminalSearchStatus.Pending, processor.PopulateSearchMatchesAsync("old", matches));
        Assert.Empty(matches);
        processor.Process("new needle"u8);
        Assert.Equal(TerminalSearchStatus.Pending, processor.PopulateSearchMatchesAsync("new", matches));
        Assert.Empty(matches);
        await Complete(processor, "new", matches);
        Assert.Single(matches);
        Assert.Null(processor.SearchError);
        List<TerminalSearchMatch> expected = [];
        processor.PopulateSearchMatches("new", expected);
        Assert.Equal(expected, matches);
        Assert.True(processor.RefreshTimedState());
        processor.CancelSearch();
        Assert.Equal(TerminalSearchStatus.Complete, processor.PopulateSearchMatchesAsync("", matches));
        Assert.Empty(matches);
    }

    [Fact]
    public async Task BackgroundSearchReadsOnlyPublishedSynchronizedOutput()
    {
        using BasicVtProcessor processor = new(new(40, 4, 2000));
        for (int i = 0; i < 600; i++) processor.Process("history\r\n"u8);
        processor.Process("before\u001b[?2026h\r\u001b[2Kafter"u8);
        List<TerminalSearchMatch> matches = [];
        await Complete(processor, "before", matches);
        Assert.Single(matches);
        await Complete(processor, "after", matches);
        Assert.Empty(matches);
        processor.Process("\u001b[?2026l"u8);
        await Complete(processor, "after", matches);
        Assert.Single(matches);
    }

    [Fact]
    public void DisposalJoinsWorkerWhileScreenLockIsHeld()
    {
        TerminalScreen screen = new(120, 4, 10000);
        BasicVtProcessor processor = new(screen);
        for (int i = 0; i < 8000; i++) processor.Process("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\r\n"u8);
        lock (screen.SyncRoot)
        {
            processor.PopulateSearchMatchesAsync("aaaaab", []);
            processor.Dispose();
        }
        Assert.Null(processor.NextTimedRefreshDelay);
        processor.Dispose();
    }

    [Fact]
    public void SmallScreenFinishesInlineAndReturnsOwnedResults()
    {
        using BasicVtProcessor processor = new(new(40, 4));
        processor.Process("needle"u8);
        List<TerminalSearchMatch> matches = [];
        Assert.Equal(TerminalSearchStatus.Complete, processor.PopulateSearchMatchesAsync("needle", matches));
        Assert.Single(matches);
        matches.Clear();
        processor.PopulateSearchMatchesAsync("needle", matches);
        Assert.Single(matches);
        Assert.Null(processor.SearchError);
        Assert.Null(processor.NextTimedRefreshDelay);
    }

    private static async Task Complete(BasicVtProcessor processor, string needle, List<TerminalSearchMatch> matches)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (processor.PopulateSearchMatchesAsync(needle, matches) == TerminalSearchStatus.Pending)
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "Background search did not complete.");
            await Task.Delay(1);
        }
        Assert.Null(processor.SearchError);
    }
}
