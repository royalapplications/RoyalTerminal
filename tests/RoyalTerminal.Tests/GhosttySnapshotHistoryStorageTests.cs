// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotHistoryStorageTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 9)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(4, 7)]
    [InlineData(8, 3)]
    public void CircularPrependRetainsOrderAndExistingRowIdentity(int capacity, int prefixLength)
    {
        TerminalRowBuffer rows = new(capacity);
        for (int i = 0; i < capacity; i++) rows.Add(Row('a' + i));
        if (capacity > 0) { rows.RemoveFirst(); rows.Add(Row('Z')); }
        TerminalRow[] existing = new TerminalRow[rows.Count];
        for (int i = 0; i < existing.Length; i++) existing[i] = rows[i];
        TerminalRow[] prefix = new TerminalRow[prefixLength];
        for (int i = 0; i < prefix.Length; i++) prefix[i] = Row('0' + i);
        rows.PrependRange(prefix);
        Assert.Equal(existing.Length + prefix.Length, rows.Count);
        for (int i = 0; i < prefix.Length; i++) Assert.Same(prefix[i], rows[i]);
        for (int i = 0; i < existing.Length; i++) Assert.Same(existing[i], rows[i + prefix.Length]);
        rows.Add(Row('!'));
        Assert.Equal('!', rows[rows.Count - 1][0].Codepoint);
    }

    [Fact]
    public void CircularPrependWithinCapacityAllocatesNothingAndRejectsNullBeforeMutation()
    {
        TerminalRowBuffer rows = new(8);
        TerminalRow[] prefix = [Row('P'), Row('Q')];
        rows.Add(Row('A')); rows.Add(Row('B'));
        rows.PrependRange(prefix); rows.RemoveFirst(2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { rows.PrependRange(prefix); rows.RemoveFirst(2); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Throws<ArgumentNullException>(() => rows.PrependRange([Row('X'), null!]));
        Assert.Equal(2, rows.Count);
        Assert.Equal('A', rows[0][0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryPrependKeepsViewportAndAnchorsOnTheirOriginalCells(bool dormant)
    {
        TerminalScreen screen = new(4, 2, 100);
        for (int i = 0; i < 4; i++) screen.AddRow();
        screen.ScrollOffset = 2;
        TerminalRow visible = screen.GetViewportRow(0);
        visible[0].Codepoint = 'V';
        TerminalScreenAnchor anchor = screen.CreateAnchor(3, 1);
        if (dormant) screen.SwitchToAlternateBuffer(clear: true);
        TerminalRow activeBefore = screen.GetViewportRow(0);
        TerminalScreen owner = new(2, 1);
        TerminalRow history = new(2);
        history[0].Codepoint = 'H'; history[0].HyperlinkId = owner.RegisterHyperlink([255], [128], 0);
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([history], owner, 2);
        Assert.Equal(1, screen.PrependSnapshotHistory(0, page));
        Assert.Same(activeBefore, screen.GetViewportRow(0));
        Assert.Equal(2, screen.GetSnapshotRows(0)![0].Columns); // Never flatten mixed history widths.
        Assert.True(screen.TryGetHyperlink(screen.GetSnapshotRows(0)![0][0].HyperlinkId, out TerminalHyperlink? link));
        Assert.Equal(new byte[] { 255 }, link!.UriBytes.ToArray());
        if (dormant) screen.SwitchToPrimaryBuffer();
        Assert.Same(visible, screen.GetViewportRow(0));
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new TerminalGridPosition(1, 4), position);
    }

    [Fact]
    public void FailedPagePublicationDoesNotLeakRowsOrHyperlinkRegistrations()
    {
        TerminalScreen screen = new(4, 2);
        TerminalScreen owner = new(1, 1);
        TerminalRow row = Row('H'); row[0].HyperlinkId = owner.RegisterHyperlink("new"u8, [], 1);
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([row], owner, 1);
        // A checked raster-anchor overflow happens after PAGE decode/link registration
        // but before row or registry commit, giving a deterministic failure point.
        screen.ReplaceRasterImage(new TerminalRasterImageSource(1, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
            new TerminalRasterImagePlacement(1, TerminalRasterImageLayer.AboveText, 0, int.MaxValue, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        TerminalRow original = screen.GetRow(0);
        Assert.Throws<OverflowException>(() => screen.PrependSnapshotHistory(0, page));
        Assert.Equal(2, screen.TotalRows);
        Assert.Same(original, screen.GetRow(0));
        Assert.False(screen.TryGetHyperlink(1, out _));
        Assert.Equal(1, screen.RegisterHyperlink("after"u8, [], 2));
        Assert.Throws<InvalidOperationException>(() => screen.PrependSnapshotHistory(1, page));
    }

    [Fact]
    public void UnlinkedHistoryInsertionDoesNotCopyExistingRowMetadata()
    {
        TerminalScreen screen = new(80, 24, 10000);
        for (int i = 0; i < 10000; i++) screen.AddRow();
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([new TerminalRow(80)], screen, 80);
        new TerminalScreen(80, 24).PrependSnapshotHistory(0, page);
        TerminalRow previousFirst = screen.GetRow(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        screen.PrependSnapshotHistory(0, page);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 10000); // One decoded row, not 10,024 metadata copies.
        Assert.Same(previousFirst, screen.GetRow(1));
        output.WriteLine($"History prepend allocation with 10,024 existing rows: {allocated}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RasterAndTrackedAnchorsShiftOnlyInTheirOwningBufferAndCopy(bool dormant)
    {
        TerminalScreen source = new(4, 2);
        source.ReplaceRasterImage(new TerminalRasterImageSource(1, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
            new TerminalRasterImagePlacement(1, TerminalRasterImageLayer.AboveText, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
        TerminalScreenAnchor anchor = source.CreateAnchor(0, 0);
        if (dormant) source.SwitchToAlternateBuffer(clear: true);
        TerminalScreen copy = source.CreateStateCopy();
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([Row('H')], copy, 1);
        copy.PrependSnapshotHistory(0, page);
        if (dormant) { source.SwitchToPrimaryBuffer(); copy.SwitchToPrimaryBuffer(); }
        Assert.Equal(0, source.GetRasterImagePlacements()[0].AnchorRow);
        Assert.Equal(1, copy.GetRasterImagePlacements()[0].AnchorRow);
        Assert.True(source.TryResolveAnchor(anchor, out TerminalGridPosition original));
        Assert.True(copy.TryResolveAnchor(anchor, out TerminalGridPosition moved));
        Assert.Equal(0, original.Row);
        Assert.Equal(1, moved.Row);
    }

    [Fact]
    public void NativeGoldenHistoryPagesJoinStagedRowsInTheSameOrder()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native history storage differential available: {available}");
        if (!available) return;
        byte[] source = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using GhosttySnapshotStateReader reader = new(source, new());
        TerminalScreen staged = GhosttySnapshotLiveScreen.Stage(reader.ReadReady(), TerminalTheme.Dark, 100);
        using GhosttySnapshotDecoder nativeReader = new(source);
        using GhosttyTerminal native = nativeReader.Ready();
        while (reader.ReadNextHistoryPage() is { } history)
        {
            Assert.True(nativeReader.Next());
            int added = staged.PrependSnapshotHistory(history.Key, history.Page);
            Assert.Equal((nuint)added, nativeReader.GetProgressRows());
            Assert.Equal(history.Remaining, nativeReader.GetProgressRemaining());
        }
        Assert.False(nativeReader.Next());
        using GhosttySnapshotStateReader full = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotReadyState ready = full.ReadReady();
        TerminalScreen expected = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 100);
        while (full.ReadNextHistoryPage() is { } history) expected.PrependSnapshotHistory(history.Key, history.Page);
        for (int key = 0; key < 2; key++)
        {
            TerminalRowBuffer a = staged.GetSnapshotRows(key)!, b = expected.GetSnapshotRows(key)!;
            Assert.Equal(b.Count, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(b[i].Columns, a[i].Columns);
                Assert.Equal(b[i].ReadOnlyCells.ToArray(), a[i].ReadOnlyCells.ToArray());
            }
        }
    }

    [Theory]
    [InlineData(0, true)] // No changes.
    [InlineData(1, true)] // Height-only resize retains the screen and current width.
    [InlineData(2, false)] // Current width differs from READY.
    [InlineData(3, true)] // Width restored before the next page is consumed.
    [InlineData(4, true)] // RIS resets primary contents, not its ScreenSet generation.
    public void NativeHistoryDecisionUsesCurrentWidthAndScreenGeneration(int operation, bool accepted)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native history applicability reference available: {available}");
        if (!available) return;
        byte[] source = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using GhosttySnapshotStateReader wire = new(source, new());
        wire.ReadReady();
        GhosttySnapshotHistoryPage page = wire.ReadNextHistoryPage()!.Value;
        using GhosttySnapshotDecoder decoder = new(source);
        using GhosttyTerminal terminal = decoder.Ready();
        switch (operation)
        {
            case 1: terminal.Resize(2, 4); break;
            case 2: terminal.Resize(3, 3); break;
            case 3: terminal.Resize(3, 3); terminal.Resize(2, 3); break;
            case 4: terminal.Write("\u001bc"u8); break;
        }
        Assert.True(decoder.Next());
        Assert.Equal(accepted ? (nuint)page.Page.Grid.Rows : 0, decoder.GetProgressRows());
        if (!accepted)
        {
            // Restoring width after a gap must not re-enable the older sequence.
            terminal.Resize(2, 3);
            while (decoder.Next()) Assert.Equal((nuint)0, decoder.GetProgressRows());
        }
    }

    private static TerminalRow Row(int codepoint)
    {
        TerminalRow row = new(1); row[0].Codepoint = codepoint; return row;
    }
}
