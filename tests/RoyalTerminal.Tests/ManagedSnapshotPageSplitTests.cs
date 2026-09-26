// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.splitForCapacity / PageList.split / Page.exactRowCapacity
// define metadata pressure, source retention and suffix cloning. WT's circular
// TextBuffer and xterm.js Buffer/CircularList have no PAGE allocator equivalent.
// Preserve their visible row/anchor order while following Ghostty's split rule.
public sealed class ManagedSnapshotPageSplitTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);

    [Theory]
    [InlineData(4, 1, 2, false)] // Smaller upper half: pin stays above the split.
    [InlineData(4, 2, 2, true)] // Smaller lower half: pin moves with the suffix.
    [InlineData(3, 1, 1, true)] // Equal layout sizes: native chooses the suffix.
    public void SplitUsesExactRowLayoutAndRetainsUpperAllocation(int rowCount, int cursor, int split, bool moves)
    {
        GhosttySnapshotPageCapacity capacity = new(1024, 8, 16, 192, 1024, 2048);
        GhosttySnapshotPageAllocation page = new(capacity), original = page;
        GhosttySnapshotPageTracker tracker = new();
        GhosttySnapshotPageTracker.State state = new(new(capacity));
        List<TerminalRow> rows = Rows(rowCount, page);
        foreach (TerminalRow row in rows)
        {
            state.ObserveSlot(row.SnapshotAllocationRow);
            state.Revisions[row.SnapshotAllocationRow] = row.SnapshotMetadataRevision;
            Assert.Equal(GhosttySnapshotSetAddResult.Success,
                state.Storage.Styles.ChangeCell(row.SnapshotAllocationRow * capacity.Columns, Bold));
        }
        GhosttySnapshotPageTracker.State source = state;
        object identity = state.Storage.AllocationIdentity;
        TerminalRow[] originalRows = rows.ToArray();
        List<TerminalRow> targetRows = rows;
        Assert.True(tracker.SplitForCapacity(ref page, ref state, ref targetRows, rows[cursor], new(4096)));
        Assert.Same(identity, source.Storage.AllocationIdentity);
        Assert.Same(original, originalRows[0].SnapshotAllocation);
        GhosttySnapshotPageAllocation lower = originalRows[split].SnapshotAllocation!;
        Assert.NotSame(original, lower);
        Assert.Equal(capacity, lower.Capacity);
        Assert.Same(moves ? lower : original, page);
        Assert.Equal(moves ? rowCount - split : split, targetRows.Count);
        for (int index = 0; index < rowCount; index++)
        {
            Assert.Same(originalRows[index], rows[index]);
            Assert.Equal(index < split ? index : index - split, rows[index].SnapshotAllocationRow);
            Assert.Equal(index < split ? Bold : default, source.Storage.Styles.CellStyle(index * capacity.Columns));
        }
        Assert.Equal(split, source.NextRowSlot);
    }

    [Fact]
    public void SplitClonesLiveMetadataButRetainsDeadStringsAndTemporaryReferencesOnlyInSource()
    {
        GhosttySnapshotPageCapacity capacity = new(1024, 4, 8, 192, 1024, 2048);
        GhosttySnapshotPageAllocation page = new(capacity);
        GhosttySnapshotPageTracker tracker = new();
        GhosttySnapshotPageTracker.State state = new(new(capacity));
        List<TerminalRow> rows = Rows(3, page);
        byte[] live = Link("live"), cursor = Link("cursor");
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, state.Storage.Hyperlinks.ObserveCell(1024, live));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, state.Storage.Hyperlinks.StartCursor(cursor));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, state.Storage.Styles.ChangeCell(1024, Bold));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, state.Storage.Styles.ChangeCursor(Italic));
        int temporaryStyle = state.Storage.Styles.SuspendCursorReference();
        int temporaryLink = state.Storage.Hyperlinks.SuspendCursorReference();
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, state.Storage.Graphemes.Set(1024, 5));
        foreach (TerminalRow row in rows) state.ObserveSlot(row.SnapshotAllocationRow);
        GhosttySnapshotPageTracker.State original = state;
        GhosttySnapshotPageStorage retained = state.Storage.Copy();
        List<TerminalRow> group = rows;

        Assert.True(tracker.SplitForCapacity(ref page, ref state, ref group, rows[1], new(4096)));
        Assert.NotSame(original, state);
        Assert.NotSame(original.Storage.AllocationIdentity, state.Storage.AllocationIdentity);
        Assert.Equal(Bold, state.Storage.Styles.CellStyle(0));
        Assert.Equal(default, state.Storage.Styles.Cursor);
        Assert.Equal(1, state.Storage.Styles.Count);
        Assert.Equal(1, state.Storage.Hyperlinks.Count);
        Assert.Equal(32UL, state.Storage.Hyperlinks.StringBytes);
        Assert.Equal(0, state.Storage.Hyperlinks.CursorId);
        Assert.Equal(5, state.Storage.Graphemes.SuffixLength(0));
        Assert.Equal(0, original.Storage.Graphemes.Count);
        Assert.Equal(64UL, original.Storage.Hyperlinks.StringBytes); // Removed cells' strings stay allocated.
        original.Storage.Styles.RestoreCursorReference(temporaryStyle);
        original.Storage.Hyperlinks.RestoreCursorReference(temporaryLink);
        Assert.Equal(Italic, original.Storage.Styles.Cursor);
        Assert.Equal(cursor, original.Storage.Hyperlinks.CursorEncoding.ToArray());
        Assert.Equal(Bold, retained.Styles.CellStyle(1024));
        Assert.Equal(5, retained.Graphemes.SuffixLength(1024));
        Assert.Equal(2, retained.Styles.Count);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)] // Alignment ties at row zero produce native's no-op split.
    public void UnsplittableRangeDoesNotReplaceOrClearStorage(int count, int cursor)
    {
        GhosttySnapshotPageAllocation page = new(new(8, 4, 8, 0, 0, 0)), original = page;
        GhosttySnapshotPageTracker.State state = new(new(page.Capacity));
        List<TerminalRow> group = Rows(count, page);
        GhosttySnapshotPageTracker tracker = new();
        Assert.Equal(GhosttySnapshotSetAddResult.Success, state.Storage.Styles.ChangeCell(0, Bold));
        GhosttySnapshotPageStorage storage = state.Storage;
        Assert.False(tracker.SplitForCapacity(ref page, ref state, ref group, group[cursor], new(4096)));
        Assert.Same(original, page);
        Assert.Same(storage, state.Storage);
        Assert.Equal(Bold, storage.Styles.CellStyle(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SgrAtSaturatedStyleCapacitySplitsAndMigratesCursorLinkBeforePrinting(bool explicitId)
    {
        // Like upstream's max-style tripwire test, retained table references
        // saturate the allocator without constructing a huge visible terminal.
        GhosttySnapshotStyleStorage styles = FullStyles();
        GhosttySnapshotPageAllocation page = new(new(8, 8, ushort.MaxValue, 192, 1024, 2048), styles,
            restoredGraphemes: new(1024), restoredHyperlinks: new(192, 2048));
        TerminalScreen owner = new(8, 3);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 3, 100, owner.Theme);
        TerminalRow[] rows = Rows(3, page).ToArray();
        foreach (TerminalRow row in rows) row.SnapshotAllocationUnmodified = true;
        screen.InstallSnapshotRows(rows, null, 0);
        using BasicVtProcessor processor = new(screen);
        string open = $"\u001b[3;1H\u001b]8;{(explicitId ? "id=stable" : "")};uri\u001b\\";
        processor.Process(Encoding.ASCII.GetBytes(open));
        int previousLink = screen.SnapshotCursorHyperlinkToken(0, 0);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalScreenAnchor anchor = screen.CreateAnchor(2, 0);
        processor.Process("\u001b[1m"u8);

        Assert.Same(page, screen.GetViewportRow(0).SnapshotAllocation);
        TerminalRow target = screen.GetViewportRow(2);
        Assert.NotSame(page, target.SnapshotAllocation);
        Assert.False(target.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal(0, target.SnapshotAllocationRow);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 2, Bold));
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new TerminalGridPosition(0, 2), position);
        int currentLink = screen.SnapshotCursorHyperlinkToken(0, 0);
        Assert.Equal(explicitId, previousLink == currentLink);
        Assert.True(screen.TryGetHyperlink(currentLink, out TerminalHyperlink? link));
        Assert.Equal(explicitId ? 0U : 1U, link!.ImplicitId);
        Assert.Same(page, retained.GetViewportRow(2).SnapshotAllocation);
        Assert.Equal(previousLink, retained.SnapshotCursorHyperlinkToken(0, 0));
        processor.Process("X"u8);
        Assert.Equal(CellAttributes.Bold, target.ReadOnlyCells[0].Attributes);
        Assert.Equal(currentLink, target.ReadOnlyCells[0].HyperlinkId);
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        GhosttySnapshotScreenState cursor = reader.ReadReady().Screens[0].State;
        Assert.Equal(explicitId ? 0U : 2U, cursor.HyperlinkImplicitCounter);
    }

    [Fact]
    public void ExactLayoutAtMaximumStyleCountDoesNotWrapTheRequestedCapacity()
    {
        // Native capacityForCount(53247) is 65536, although a requested 65535
        // already has that exact table layout. Saturate the requested count,
        // not the layout charge; the pinned Zig cast cannot represent it.
        GhosttySnapshotStyleStorage styles = FullStyles(attachCells: true);
        GhosttySnapshotPageAllocation page = new(new(ushort.MaxValue, 4, ushort.MaxValue, 0, 0, 0)), original = page;
        GhosttySnapshotPageTracker.State state = new(new(styles, new(0), new(0, 0)));
        GhosttySnapshotPageTracker tracker = new();
        List<TerminalRow> rows = Rows(4, page), group = rows;
        foreach (TerminalRow row in rows) state.ObserveSlot(row.SnapshotAllocationRow);
        GhosttySnapshotPageTracker.State source = state;
        Assert.True(tracker.SplitForCapacity(ref page, ref state, ref group, rows[1], new(4096)));
        // Without saturation the upper requested style count wraps to zero:
        // two rows appear cheaper than three, leaving the cursor above the
        // split. The real style charge makes the three-row suffix cheaper.
        Assert.NotSame(original, page);
        Assert.Equal(53247, source.Storage.Styles.Count);
        Assert.Equal(53247, source.Storage.Styles.CellCount);
        Assert.Equal(0, state.Storage.Styles.Count);
        Assert.Equal(ushort.MaxValue, page.Capacity.Styles);
    }

    [Theory]
    [InlineData(1, 8, 0, false)] // One-row page cannot split.
    [InlineData(2, 8, 0, false)] // Layout tie chooses native's zero-row no-op.
    [InlineData(4, 1024, 1, true)] // Split succeeds, but upper table references remain full.
    public void RefusedSgrKeepsPreviousPenAndDoesNotPoisonPage(int rowCount, int columns, int cursor, bool split)
    {
        TerminalScreen screen = PressureScreen(rowCount, columns, out GhosttySnapshotPageAllocation page);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes($"\u001b[{cursor + 1};1H\u001b[38;2;1;0;0m\u001b]8;;uri\u001b\\"));
        int link = screen.SnapshotCursorHyperlinkToken(0, 0);
        TerminalScreen retained = screen.CreateStateCopy();
        processor.Process("\u001b[1mX"u8);

        Assert.Same(page, screen.GetViewportRow(cursor).SnapshotAllocation);
        Assert.False(page.MetadataOverflow);
        Assert.Equal(split, !ReferenceEquals(page, screen.GetViewportRow(rowCount - 1).SnapshotAllocation));
        GhosttySnapshotStyle previous = new(new(2, 1, 0, 0), default, default, 0);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, cursor, previous));
        Assert.Equal(previous, GhosttySnapshotLivePage.EncodeStyle(screen.GetViewportRow(cursor).ReadOnlyCells[0]));
        Assert.Equal(link, screen.GetViewportRow(cursor).ReadOnlyCells[0].HyperlinkId);
        Assert.Equal(0, retained.GetViewportRow(cursor).ReadOnlyCells[0].Codepoint);
        processor.Process("\u001b[0mY"u8);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, cursor, default));
        Assert.Equal(default, GhosttySnapshotLivePage.EncodeStyle(screen.GetViewportRow(cursor).ReadOnlyCells[1]));
    }

    [Fact]
    public void RefusedStyleOnPageCrossingUsesDefaultAndStillMigratesImplicitLink()
    {
        TerminalScreen owner = new(8, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 2, 100, owner.Theme);
        GhosttySnapshotPageAllocation target = PressurePage(8);
        TerminalRow first = new(8) { SnapshotAllocation = new(new(8, 1, 16, 192, 1024, 2048)) };
        TerminalRow second = new(8) { SnapshotAllocation = target, SnapshotAllocationUnmodified = true };
        screen.InstallSnapshotRows([first, second], null, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1m\u001b]8;;uri\u001b\\A"u8);
        int oldLink = first.ReadOnlyCells[0].HyperlinkId;
        processor.Process("\u001b[2;1HX"u8);

        Assert.Same(target, second.SnapshotAllocation);
        Assert.False(target.MetadataOverflow);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 1, default));
        Assert.Equal(CellAttributes.None, second.ReadOnlyCells[0].Attributes);
        int newLink = second.ReadOnlyCells[0].HyperlinkId;
        Assert.NotEqual(oldLink, newLink);
        Assert.True(screen.TryGetHyperlink(newLink, out TerminalHyperlink? link));
        Assert.Equal(1U, link!.ImplicitId);
        Assert.Equal(CellAttributes.Bold, first.ReadOnlyCells[0].Attributes);
        Assert.Equal(oldLink, first.ReadOnlyCells[0].HyperlinkId);
    }

    [Fact]
    public void RefusedSavedCursorStyleFallsBackToDefaultWithoutLosingPositionOrLink()
    {
        TerminalScreen screen = new(8, 1) { SnapshotScrollbackQuota = new() };
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1;4H\u001b[1m\u001b7\u001b[0m"u8);
        screen.AdoptStateFrom(PressureScreen(1, 8, out GhosttySnapshotPageAllocation page));
        processor.Process("\u001b[1;1H\u001b[38;2;1;0;0m\u001b]8;;uri\u001b\\"u8);
        int link = screen.SnapshotCursorHyperlinkToken(0, 0);
        processor.Process("\u001b8X"u8);

        Assert.Same(page, screen.GetViewportRow(0).SnapshotAllocation);
        Assert.False(page.MetadataOverflow);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 0, default));
        TerminalCell cell = screen.GetViewportRow(0).ReadOnlyCells[3];
        Assert.Equal('X', cell.Codepoint);
        Assert.Equal(default, GhosttySnapshotLivePage.EncodeStyle(cell));
        Assert.Equal(link, cell.HyperlinkId);
    }

    private static TerminalScreen PressureScreen(int count, int columns, out GhosttySnapshotPageAllocation page)
    {
        TerminalScreen owner = new(columns, count);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(columns, count, 100, owner.Theme);
        page = PressurePage(columns);
        TerminalRow[] rows = Rows(count, page).ToArray();
        foreach (TerminalRow row in rows) row.SnapshotAllocationUnmodified = true;
        screen.InstallSnapshotRows(rows, null, 0);
        return screen;
    }

    private static GhosttySnapshotPageAllocation PressurePage(int columns)
        => new(new((ushort)columns, 8, ushort.MaxValue, 192, 1024, 2048), FullStyles(),
            restoredGraphemes: new(1024), restoredHyperlinks: new(192, 2048));

    private static GhosttySnapshotStyleStorage FullStyles(bool attachCells = false)
    {
        GhosttySnapshotStyleStorage styles = new(ushort.MaxValue);
        bool[] buckets = new bool[ushort.MaxValue + 1];
        int capacity = (int)GhosttySnapshotAllocation.SetItemCapacity(ushort.MaxValue);
        // Distinct initial buckets avoid the independent max-probe failure and
        // deterministically exercise the actual count boundary. Bounded search.
        for (int value = 1; value <= 1_000_000 && styles.Count < capacity; value++)
        {
            GhosttySnapshotStyle style = new(new(2, (byte)value, (byte)(value >> 8), (byte)(value >> 16)), default, default, 0);
            int bucket = (int)(GhosttySnapshotMetadataHash.Style(style) & ushort.MaxValue);
            if (buckets[bucket]) continue;
            buckets[bucket] = true;
            int id = styles.AddTableReference(style);
            Assert.NotEqual(0, id);
            if (attachCells)
            {
                styles.AttachDecodedCell(styles.Count - 1, id);
                styles.ReleaseTableReference(id);
            }
        }
        Assert.Equal(capacity, styles.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, styles.ChangeCursor(Bold));
        return styles;
    }

    private static List<TerminalRow> Rows(int count, GhosttySnapshotPageAllocation page)
    {
        List<TerminalRow> rows = new(count);
        for (int index = 0; index < count; index++)
            rows.Add(new(page.Capacity.Columns) { SnapshotAllocation = page, SnapshotAllocationRow = index });
        return rows;
    }

    private static byte[] Link(string uri)
    {
        using MemoryStream stream = new();
        new GhosttySnapshotHyperlink(false, 0, default, Encoding.ASCII.GetBytes(uri)).WriteTo(stream);
        return stream.ToArray();
    }
}
