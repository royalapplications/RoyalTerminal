// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Reference decision: Ghostty Screen.appendGrapheme/startHyperlink/
// cursorSetHyperlink and Terminal.print define these non-transactional failure
// boundaries. WT ROW text storage and xterm.js combined strings have no matching
// PAGE limit. Use representable near-four-GiB logical capacity hints to refuse
// growth deterministically, without allocating huge CLR/native pages or relying
// on actual process OOM. Existing native continuation tests cover successful growth.
public sealed class ManagedSnapshotMetadataFailureTests
{
    [Theory]
    [InlineData(4096, 0, false)]
    [InlineData(4096, 4, false)]
    [InlineData(4096, 0, true)]
    [InlineData(4096, 4, true)]
    [InlineData(16384, 0, false)]
    [InlineData(16384, 4, false)]
    [InlineData(16384, 0, true)]
    [InlineData(16384, 4, true)]
    public void RefusedAppendRetainsTextAndCowOwnershipAndCanRecover(int alignment, int suffix, bool clustering)
    {
        GhosttySnapshotPageCapacity capacity = CeilingCapacity(GhosttySnapshotCapacityDimension.GraphemeBytes, alignment);
        TerminalScreen screen = Screen(8, alignment, capacity);
        TerminalRow row = screen.GetViewportRow(0);
        for (int i = 0; i < 3; i++) SetGrapheme(row, i, 64);
        SetGrapheme(row, 3, suffix == 0 ? 64 : 56);
        SetGrapheme(row, 4, suffix);
        using BasicVtProcessor processor = new(screen);
        Process(processor, (clustering ? "\u001b[?2027h" : "") + "\u001b[1;6H");
        GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();
        string? previous = row.ReadOnlyCells[4].Grapheme;
        ulong used = suffix == 0 ? 1024UL : 1008UL;

        Process(processor, "\u0302");

        Assert.Equal(previous, row.ReadOnlyCells[4].Grapheme);
        Assert.Equal('A', row.ReadOnlyCells[4].Codepoint);
        Assert.Equal(5, processor.CursorCol);
        Assert.Same(page, row.SnapshotAllocation);
        Assert.False(page.MetadataOverflow);
        Assert.Equal((suffix == 0 ? 4UL : 5UL, used), GraphemeUsage(screen, row));
        Assert.Equal(previous, retained.GetViewportRow(0).ReadOnlyCells[4].Grapheme);

        Process(processor, "\u001b[1;1H\u001b[X\u001b[1;6H\u0302");

        Assert.Equal((previous ?? "A") + "\u0302", row.ReadOnlyCells[4].Grapheme);
        Assert.Same(page, row.SnapshotAllocation);
        Assert.Equal((4UL, used - 240), GraphemeUsage(screen, row));
        Assert.Equal((suffix == 0 ? 4UL : 5UL, used), GraphemeUsage(retained, retained.GetViewportRow(0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedAppendRetainsEarlierWidthTailAndCursorChanges(bool narrow)
    {
        TerminalScreen screen = Screen(8, 4096, CeilingCapacity(GhosttySnapshotCapacityDimension.GraphemeBytes));
        TerminalRow row = screen.GetViewportRow(0);
        for (int i = 0; i < 4; i++) SetGrapheme(row, i, 64);
        row[4].Codepoint = narrow ? 0x1F600 : 0x2764;
        row[4].Width = narrow ? (byte)2 : (byte)1;
        row[5].Codepoint = narrow ? 0 : 'T';
        row[5].Width = narrow ? (byte)0 : (byte)1;
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[1;" + (narrow ? "7" : "6") + "H");
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, narrow ? "\ufe0e" : "\ufe0f");

        Assert.Null(row.ReadOnlyCells[4].Grapheme);
        Assert.Equal(narrow ? (byte)1 : (byte)2, row.ReadOnlyCells[4].Width);
        Assert.Equal(narrow ? (byte)1 : (byte)0, row.ReadOnlyCells[5].Width);
        Assert.Equal(0, row.ReadOnlyCells[5].Codepoint);
        Assert.Equal(narrow ? 5 : 6, processor.CursorCol);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal((4UL, 1024UL), GraphemeUsage(screen, row));
        Assert.Equal(narrow ? (byte)2 : (byte)1, retained.GetViewportRow(0).ReadOnlyCells[4].Width);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void RefusedCrossPageTransferRetainsSourceAndScalarPrefixAndStopsBeforeTail(bool occupiedTail, bool supplementary, bool remapped)
    {
        GhosttySnapshotPageCapacity target = CeilingCapacity(GhosttySnapshotCapacityDimension.GraphemeBytes);
        TerminalScreen screen = Screen(8, 4096, new(8, 1, 16, 192, 1024, 2048), target);
        TerminalRow source = screen.GetViewportRow(0), destination = screen.GetViewportRow(1);
        SetGrapheme(destination, occupiedTail ? 1 : 2, 64);
        SetGrapheme(destination, 3, 64);
        SetGrapheme(destination, 4, 8);
        using BasicVtProcessor processor = new(screen);
        string mark = supplementary ? "\U0001D185" : "\u0301";
        string prefix = "\u263a" + string.Concat(Enumerable.Repeat(mark, 62)) + "\u200d";
        Process(processor, "\u001b[?2027h\u001b[1;8H" + prefix);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalCell tail = destination.ReadOnlyCells[1];

        Process(processor, (remapped ? "\u001b(0" : "") + "\u2764");

        Assert.Equal((remapped ? " " : "\u263a") + string.Concat(Enumerable.Repeat(mark, 60)), destination.ReadOnlyCells[0].Grapheme);
        Assert.Equal(remapped ? ' ' : 0x263A, destination.ReadOnlyCells[0].Codepoint);
        Assert.Equal((byte)2, destination.ReadOnlyCells[0].Width);
        Assert.Equal(tail.Grapheme, destination.ReadOnlyCells[1].Grapheme);
        Assert.Equal(tail.Width, destination.ReadOnlyCells[1].Width);
        Assert.Equal(prefix, source.ReadOnlyCells[7].Grapheme);
        Assert.Equal(0, source.ReadOnlyCells[7].Codepoint);
        Assert.True(source.ReadOnlyCells[7].IsWideSpacerHead);
        Assert.Equal(0, processor.CursorCol);
        Assert.Equal(1, processor.CursorRow);
        Assert.False(source.SnapshotAllocation!.MetadataOverflow);
        Assert.False(destination.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal((1UL, 256UL), GraphemeUsage(screen, source));
        Assert.Equal((4UL, 784UL), GraphemeUsage(screen, destination));
        Assert.Equal((3UL, 544UL), GraphemeUsage(retained, retained.GetViewportRow(1)));
        Assert.Equal(prefix, retained.GetViewportRow(0).ReadOnlyCells[7].Grapheme);

        // A later overwrite releases only the copied prefix, not its source.
        Process(processor, "X");
        Assert.Equal(prefix, source.ReadOnlyCells[7].Grapheme);
        Assert.Null(destination.ReadOnlyCells[0].Grapheme);
        Assert.Equal((1UL, 256UL), GraphemeUsage(screen, source));
    }

    [Fact]
    public void RefusedFirstTransferredScalarLeavesNoDestinationSuffixAndDoesNotReleaseSource()
    {
        TerminalScreen screen = Screen(8, 4096, new(8, 1, 16, 192, 1024, 2048),
            CeilingCapacity(GhosttySnapshotCapacityDimension.GraphemeBytes));
        TerminalRow source = screen.GetViewportRow(0), destination = screen.GetViewportRow(1);
        for (int i = 1; i <= 4; i++) SetGrapheme(destination, i, 64);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[1;8H\u263a\u200d\u2764");

        Assert.Null(destination.ReadOnlyCells[0].Grapheme);
        Assert.Equal(0x263A, destination.ReadOnlyCells[0].Codepoint);
        Assert.Equal((byte)2, destination.ReadOnlyCells[0].Width);
        Assert.Equal("\u263a\u200d", source.ReadOnlyCells[7].Grapheme);
        Assert.Equal((byte)1, destination.ReadOnlyCells[1].Width);
        Assert.Equal(0, processor.CursorCol);
        Assert.Equal((4UL, 1024UL), GraphemeUsage(screen, destination));
        Assert.Equal((1UL, 16UL), GraphemeUsage(screen, source));
        Assert.False(destination.SnapshotAllocation!.MetadataOverflow);
    }

    [Fact]
    public void RefusedFinalWrapAppendKeepsCompletedMoveAndTail()
    {
        GhosttySnapshotPageCapacity capacity = CeilingCapacity(GhosttySnapshotCapacityDimension.GraphemeBytes);
        TerminalScreen screen = Screen(8, 4096, capacity, capacity);
        TerminalRow source = screen.GetViewportRow(0), destination = screen.GetViewportRow(1);
        destination.SnapshotAllocation = source.SnapshotAllocation;
        destination.SnapshotAllocationRow = 1;
        for (int i = 0; i < 3; i++) SetGrapheme(source, i, 64);
        SetGrapheme(source, 3, 56);
        using BasicVtProcessor processor = new(screen);
        const string prefix = "\u263a\u0301\u0301\u0301\u200d";
        Process(processor, "\u001b[?2027h\u001b[1;8H" + prefix);
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, "\u2764");

        Assert.Equal(prefix, destination.ReadOnlyCells[0].Grapheme);
        Assert.Equal((byte)2, destination.ReadOnlyCells[0].Width);
        Assert.Equal((byte)0, destination.ReadOnlyCells[1].Width);
        Assert.Null(source.ReadOnlyCells[7].Grapheme);
        Assert.True(source.ReadOnlyCells[7].IsWideSpacerHead);
        Assert.Equal(2, processor.CursorCol);
        Assert.False(destination.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal((5UL, 1008UL), GraphemeUsage(screen, source));
        Assert.Equal(prefix, retained.GetViewportRow(0).ReadOnlyCells[7].Grapheme);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedCellHyperlinkLeavesTheCursorActiveAndCanRecover(bool stringScratchFailure)
    {
        GhosttySnapshotCapacityDimension dimension = stringScratchFailure
            ? GhosttySnapshotCapacityDimension.StringBytes : GhosttySnapshotCapacityDimension.HyperlinkBytes;
        TerminalScreen screen = Screen(120, 4096, CeilingCapacity(dimension));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open(stringScratchFailure ? new string('u', 1536) : "u") + new string('A', 102));
        TerminalRow row = screen.GetViewportRow(0);
        int token = row.ReadOnlyCells[0].HyperlinkId;
        GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, "B");

        Assert.NotEqual(0, token);
        Assert.Equal('B', row.ReadOnlyCells[102].Codepoint);
        Assert.Equal(0, row.ReadOnlyCells[102].HyperlinkId);
        Assert.Equal(token, screen.SnapshotCursorHyperlinkToken(0, -1));
        Assert.Same(page, row.SnapshotAllocation);
        Assert.False(page.MetadataOverflow);
        Assert.Equal((1UL, 102UL, stringScratchFailure ? 1536UL : 64UL), HyperlinkUsage(screen, row));
        Assert.Equal((1UL, 102UL, stringScratchFailure ? 1536UL : 32UL), HyperlinkUsage(retained, retained.GetViewportRow(0)));

        Process(processor, "\u001b[1;1H\u001b[X\u001b[1;104HC");

        Assert.Equal(token, row.ReadOnlyCells[103].HyperlinkId);
        Assert.Equal(token, screen.SnapshotCursorHyperlinkToken(0, -1));
        Assert.Equal(0, row.ReadOnlyCells[102].HyperlinkId);
        Assert.Equal(token, retained.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId);
    }

    [Fact]
    public void RefusedHyperlinkStartClosesPreviousCursorWithoutConsumingImplicitIdOrPoisoningPage()
    {
        TerminalScreen screen = Screen(8, 4096, CeilingCapacity(GhosttySnapshotCapacityDimension.StringBytes));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open(new string('u', 1984)) + "A");
        TerminalRow row = screen.GetViewportRow(0);
        int first = row.ReadOnlyCells[0].HyperlinkId;
        GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, Open(new string('v', 128)) + "B");

        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
        Assert.Equal(0, row.ReadOnlyCells[1].HyperlinkId);
        Assert.Equal(first, row.ReadOnlyCells[0].HyperlinkId);
        Assert.Same(page, row.SnapshotAllocation);
        Assert.False(page.MetadataOverflow);
        Assert.Equal((1UL, 1UL, 1984UL), HyperlinkUsage(screen, row));
        Assert.Equal(first, retained.SnapshotCursorHyperlinkToken(0, -1));

        Process(processor, Open("r") + "C");

        Assert.True(screen.TryGetHyperlink(row.ReadOnlyCells[2].HyperlinkId, out TerminalHyperlink? link));
        Assert.Equal(1U, link!.ImplicitId);
        Assert.Same(page, row.SnapshotAllocation);
    }

    [Fact]
    public void RefusedPageMigrationPublishesNoLinkAndDoesNotResurrectItOnReturn()
    {
        GhosttySnapshotPageCapacity target = CeilingCapacity(GhosttySnapshotCapacityDimension.StringBytes, stringBytes: 0);
        TerminalScreen screen = Screen(8, 4096, new(8, 1, 16, 192, 1024, 2048), target);
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "A\u001b[2;1HB");
        TerminalRow second = screen.GetViewportRow(1);
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
        Assert.Equal(0, second.ReadOnlyCells[0].HyperlinkId);
        Assert.False(second.SnapshotAllocation!.MetadataOverflow);

        Process(processor, "\u001b[1;2HC" + Open("r") + "D");

        TerminalRow first = screen.GetViewportRow(0);
        Assert.NotEqual(0, first.ReadOnlyCells[0].HyperlinkId);
        Assert.Equal(0, first.ReadOnlyCells[1].HyperlinkId);
        Assert.True(screen.TryGetHyperlink(first.ReadOnlyCells[2].HyperlinkId, out TerminalHyperlink? link));
        Assert.Equal(1U, link!.ImplicitId);
    }

    private static GhosttySnapshotPageCapacity CeilingCapacity(GhosttySnapshotCapacityDimension dimension,
        int alignment = 4096, uint stringBytes = 2048)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        for (int columns = ushort.MaxValue; columns >= 32768; columns--)
        {
            int rows = (int)(uint.MaxValue / (8UL * ((ulong)columns + 1)));
            GhosttySnapshotPageCapacity capacity = new((ushort)columns, (ushort)rows, 16, 192, 1024, stringBytes);
            // Row headers, cache-line padding and metadata must also fit.
            while (layout.LayoutBytes(capacity) > uint.MaxValue) capacity = capacity with { Rows = (ushort)(capacity.Rows - 1) };
            if (!layout.TryIncreaseCapacity(capacity, dimension, 0, 1, out _)) return capacity;
        }
        throw new InvalidOperationException("No representable growth-limited PAGE fixture was found.");
    }

    private static TerminalScreen Screen(int columns, int alignment, params GhosttySnapshotPageCapacity[] capacities)
    {
        TerminalScreen owner = new(columns, capacities.Length);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(columns, capacities.Length, 100, owner.Theme);
        TerminalRow[] rows = new TerminalRow[capacities.Length];
        for (int i = 0; i < rows.Length; i++) rows[i] = new(columns) { SnapshotAllocation = new(capacities[i]) };
        screen.InstallSnapshotRows(rows, null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        return screen;
    }

    private static void SetGrapheme(TerminalRow row, int column, int suffix)
    {
        row[column].Codepoint = 'A';
        row[column].Grapheme = suffix == 0 ? null : "A" + new string('\u0301', suffix);
    }

    private static List<TerminalRow> Group(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> rows = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) rows.Add(row);
        return rows;
    }

    private static (ulong Cells, ulong Bytes) GraphemeUsage(TerminalScreen screen, TerminalRow row)
    {
        Assert.True(screen.TryGetSnapshotGraphemeUsage(row.SnapshotAllocation!, Group(screen, row), out ulong cells, out ulong bytes));
        return (cells, bytes);
    }

    private static (ulong Links, ulong Cells, ulong Bytes) HyperlinkUsage(TerminalScreen screen, TerminalRow row)
    {
        Assert.True(screen.TryGetSnapshotHyperlinkUsage(row.SnapshotAllocation!, Group(screen, row), out ulong links, out ulong cells, out ulong bytes));
        return (links, cells, bytes);
    }

    private static string Open(string uri) => "\u001b]8;;" + uri + "\u001b\\";
    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));
}
