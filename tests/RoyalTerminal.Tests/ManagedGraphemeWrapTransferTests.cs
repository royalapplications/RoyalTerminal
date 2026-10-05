// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Reference decision: Ghostty Terminal.print's .wide branch / Page.moveGrapheme
// define transfer order and native allocation pressure. xterm.js InputHandler.print
// likewise moves the old cluster before adding the widening scalar, but copies
// cells/attributes; WT TextBuffer/ROW store row text and pad overflowing wide cells.
// Neither supplies Ghostty's same-page ownership or scalar cross-page allocator.
// Follow Ghostty: preserve the departure spacer's pen, print with the current pen,
// resolve the surviving previous row after scrolling, transfer, write tail, append.
public sealed class ManagedGraphemeWrapTransferTests
{
    [Fact]
    public void SamePageMoveKeepsTheOldSliceChargedUntilTheFinalAppend()
    {
        TerminalScreen screen = Screen();
        TerminalRow source = screen.GetViewportRow(0);
        for (int i = 0; i < 3; i++) SetGrapheme(source, i, 64);
        SetGrapheme(source, 3, 56); // 992 bytes; only two free chunks remain.
        using BasicVtProcessor processor = new(screen);
        string prefix = Prefix(4);
        Process(processor, "\u001b[?2027h\u001b[1;8H" + prefix);
        Assert.Equal((5UL, 1008UL), Usage(screen, source));
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, "\u2764");

        TerminalRow destination = screen.GetViewportRow(1);
        Assert.Same(source.SnapshotAllocation, destination.SnapshotAllocation);
        Assert.True(destination.SnapshotAllocation!.Capacity.GraphemeBytes > 1024);
        Assert.Equal((5UL, 1024UL), Usage(screen, destination));
        Assert.Equal(prefix + "\u2764", destination.ReadOnlyCells[0].Grapheme);
        Assert.Null(source.ReadOnlyCells[7].Grapheme);
        Assert.True(source.ReadOnlyCells[7].IsWideSpacerHead);
        Assert.Equal(prefix, retained.GetViewportRow(0).ReadOnlyCells[7].Grapheme);
        Assert.Equal(1024U, retained.GetViewportRow(0).SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal((5UL, 1008UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossPageTransferAppendsScalarsBeforeClearingTheTail(bool occupiedTail)
    {
        TerminalScreen screen = Screen(separatePages: true);
        TerminalRow source = screen.GetViewportRow(0), destination = screen.GetViewportRow(1);
        SetGrapheme(destination, occupiedTail ? 1 : 2, 64);
        SetGrapheme(destination, 3, 64);
        SetGrapheme(destination, 4, 8); // 544 bytes. A 256-byte clone fits.
        using BasicVtProcessor processor = new(screen);
        string prefix = Prefix(63);
        Process(processor, "\u001b[?2027h\u001b[1;8H" + prefix);
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, "\u2764");

        // Scalar transfer's 61st suffix needs the 240-byte old slice AND a
        // 256-byte replacement. It must grow even if the tail is erased later.
        Assert.True(destination.SnapshotAllocation!.Capacity.GraphemeBytes > 1024);
        Assert.Equal(1024U, source.SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal((0UL, 0UL), Usage(screen, source));
        Assert.Equal(occupiedTail ? (3UL, 544UL) : (4UL, 800UL), Usage(screen, destination));
        Assert.Equal(prefix + "\u2764", destination.ReadOnlyCells[0].Grapheme);
        Assert.Null(destination.ReadOnlyCells[1].Grapheme);
        Assert.Equal((byte)0, destination.ReadOnlyCells[1].Width);
        Assert.Equal(prefix, retained.GetViewportRow(0).ReadOnlyCells[7].Grapheme);
        Assert.Equal(1024U, retained.GetViewportRow(1).SnapshotAllocation!.Capacity.GraphemeBytes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ScrollResolvesTheSurvivingPayloadRatherThanTheOldRowObject(bool tracked, bool margins)
    {
        TerminalScreen screen = tracked ? Screen(rows: 3) : new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        string prefix = Prefix(1);
        Process(processor, "\u001b[?2027h" + (margins ? "\u001b[2;3r" : "") + "\u001b[3;8H" + prefix);
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u2764");

        int absolute = screen.GetAbsoluteRowForViewportRow(2);
        Assert.Equal(prefix + "\u2764", screen.GetRow(absolute).ReadOnlyCells[0].Grapheme);
        Assert.Null(screen.GetRow(absolute - 1).ReadOnlyCells[7].Grapheme);
        Assert.Equal(prefix, retained.GetViewportRow(2).ReadOnlyCells[7].Grapheme);
        if (tracked) Assert.Equal((1UL, 16UL), Usage(screen, screen.GetRow(absolute)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangularScrollTransfersAtTheMarginWithoutChangingOutsideCells(bool tracked)
    {
        TerminalScreen screen = tracked ? Screen(rows: 3) : new(8, 3, 10);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[2;1HL\u001b[2;8HR\u001b[3;1Hl\u001b[3;8Hr" +
            "\u001b[?2027h\u001b[?69h\u001b[2;6s\u001b[2;3r\u001b[3;6H" + Prefix(1));
        Process(processor, "\u2764");
        Assert.Equal(Prefix(1) + "\u2764", screen.GetViewportRow(2).ReadOnlyCells[1].Grapheme);
        Assert.Null(screen.GetViewportRow(1).ReadOnlyCells[5].Grapheme);
        Assert.False(screen.GetViewportRow(1).ReadOnlyCells[5].IsWideSpacerHead);
        Assert.False(screen.GetViewportRow(1).WrapsToNext);
        Assert.Equal('L', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        Assert.Equal('R', screen.GetViewportRow(1).ReadOnlyCells[7].Codepoint);
        Assert.Equal('l', screen.GetViewportRow(2).ReadOnlyCells[0].Codepoint);
        Assert.Equal('r', screen.GetViewportRow(2).ReadOnlyCells[7].Codepoint);
        if (tracked) Assert.Equal((1UL, 16UL), Usage(screen, screen.GetViewportRow(2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecycledOnlyRowDoesNotResurrectItsOldSuffix(bool tracked)
    {
        TerminalScreen screen = tracked ? Screen(rows: 1) : new(8, 1, 0);
        screen.ScrollbackLimit = 0;
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[1;8H" + Prefix(1));
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u2764");
        Assert.Equal(1, screen.TotalRows);
        Assert.Equal("\u263a\u2764", screen.GetViewportRow(0).ReadOnlyCells[0].Grapheme);
        Assert.Equal(Prefix(1), retained.GetViewportRow(0).ReadOnlyCells[7].Grapheme);
        if (tracked) Assert.Equal((1UL, 16UL), Usage(screen, screen.GetViewportRow(0)));
    }

    [Fact]
    public void ClampedIndexKeepsSameRowTransferWithoutStealingThePrecedingRow()
    {
        // The host already supports wrapping at the physical bottom below the
        // scroll region. Ghostty's unconditional up(1) is not a valid source in
        // that case; preserve this host behavior with a surviving-spacer guard.
        TerminalScreen screen = Screen(rows: 3);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[2;8HA\u0301\u001b[1;2r\u001b[3;8H" + Prefix(1));
        Process(processor, "\u2764");
        Assert.Equal(Prefix(1) + "\u2764", screen.GetViewportRow(2).ReadOnlyCells[0].Grapheme);
        Assert.Equal("A\u0301", screen.GetViewportRow(1).ReadOnlyCells[7].Grapheme);
        Assert.Null(screen.GetViewportRow(2).ReadOnlyCells[7].Grapheme);
        Assert.Equal((2UL, 32UL), Usage(screen, screen.GetViewportRow(2)));
    }

    [Fact]
    public void RemappedBaseDoesNotRetainTheOldInlineScalarInItsGraphemeText()
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[1;8H" + Prefix(1) + "\u001b(0\u2764");
        TerminalCell cell = screen.GetViewportRow(1).ReadOnlyCells[0];
        Assert.Equal(' ', cell.Codepoint);
        Assert.Equal(" \u200d\u2764", cell.Grapheme);
    }

    [Fact]
    public void DepartureSpacerRetainsItsPenWhileTheNewBaseAndTailUseTheCurrentPen()
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[1m\u001b[1\"q\u001b]8;;https://old.example\u001b\\\u001b[1;8H" + Prefix(1));
        TerminalCell original = screen.GetViewportRow(0).ReadOnlyCells[7];
        Process(processor, "\u001b[0;3m\u001b[0\"q\u001b]8;;https://new.example\u001b\\\u2764");
        TerminalCell spacer = screen.GetViewportRow(0).ReadOnlyCells[7];
        Assert.Equal(original.Attributes, spacer.Attributes);
        Assert.Equal(original.HyperlinkId, spacer.HyperlinkId);
        Assert.True(spacer.IsProtected);
        for (int col = 0; col < 2; col++)
        {
            TerminalCell cell = screen.GetViewportRow(1).ReadOnlyCells[col];
            Assert.Equal(CellAttributes.Italic, cell.Attributes);
            Assert.NotEqual(original.HyperlinkId, cell.HyperlinkId);
            Assert.NotEqual(0, cell.HyperlinkId);
            Assert.False(cell.IsProtected);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TailOverwriteClearsAnExistingWideNeighborButKeepsTheNewBase(bool occupiedTail)
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[2;" + (occupiedTail ? "2" : "1") + "H\u754c\u0301\u001b[1;8H" + Prefix(1));
        Process(processor, "\u2764");
        TerminalRow destination = screen.GetViewportRow(1);
        Assert.Equal(Prefix(1) + "\u2764", destination.ReadOnlyCells[0].Grapheme);
        Assert.Equal((byte)2, destination.ReadOnlyCells[0].Width);
        Assert.Equal((byte)0, destination.ReadOnlyCells[1].Width);
        Assert.Null(destination.ReadOnlyCells[1].Grapheme);
        Assert.Equal((byte)1, destination.ReadOnlyCells[2].Width);
        Assert.Equal((1UL, 16UL), Usage(screen, destination));
    }

    [Fact]
    public void SelectorWithoutAnExistingSuffixAllocatesOnlyOnTheNewRow()
    {
        TerminalScreen screen = Screen(separatePages: true);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[?2027h\u001b[1;8H#\ufe0f");
        Assert.Null(screen.GetViewportRow(0).ReadOnlyCells[7].Grapheme);
        Assert.Equal("#\ufe0f", screen.GetViewportRow(1).ReadOnlyCells[0].Grapheme);
        Assert.Equal((0UL, 0UL), Usage(screen, screen.GetViewportRow(0)));
        Assert.Equal((1UL, 16UL), Usage(screen, screen.GetViewportRow(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredPageWrapAndContinuationMatchNative(bool separatePages)
    {
        RequireNative();
        byte[] snapshot = Snapshot(separatePages);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        string[] inputs = ["\u001b[?2027h\u001b[1;8H" + Prefix(63), "\u2764", "\u001b[2;1H\u001b[X", "\u001b[1;8H#\ufe0f"];
        foreach (string input in inputs)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes); managed.Processor.Process(bytes);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
            int index = 0;
            foreach (GhosttySnapshotPage page in expected.Pages)
            foreach (TerminalRow reference in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(8, 2)))
            {
                TerminalRow actual = managed.Screen.GetRow(index++);
                Assert.Equal(page.Capacity.GraphemeBytes, actual.SnapshotAllocation!.Capacity.GraphemeBytes);
                Assert.Equal(page.Capacity.Styles, actual.SnapshotAllocation!.Capacity.Styles);
                for (int column = 0; column < 8; column++)
                {
                    TerminalCell a = reference.ReadOnlyCells[column], b = actual.ReadOnlyCells[column];
                    Assert.Equal(a.Codepoint, b.Codepoint);
                    Assert.Equal(a.Grapheme, b.Grapheme);
                    Assert.Equal(a.Width, b.Width);
                    Assert.Equal(a.IsWideSpacerHead, b.IsWideSpacerHead);
                }
            }
        }
    }

    private static string Prefix(int suffixes) => "\u263a" + new string('\u0301', suffixes - 1) + "\u200d";

    private static void SetGrapheme(TerminalRow row, int column, int suffixes)
    {
        row[column].Codepoint = 'A';
        row[column].Grapheme = "A" + new string('\u0301', suffixes);
    }

    private static TerminalScreen Screen(bool separatePages = false, int rows = 2)
    {
        TerminalScreen owner = new(8, rows);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, rows, 10, owner.Theme);
        GhosttySnapshotPageCapacity capacity = new(8, (ushort)(separatePages ? 1 : rows + 1), 16, 0, 1024, 0);
        GhosttySnapshotPageAllocation page = new(capacity);
        TerminalRow[] storage = new TerminalRow[rows];
        for (int i = 0; i < rows; i++)
            storage[i] = new(8) { SnapshotAllocation = separatePages ? new(capacity) : page, SnapshotAllocationRow = separatePages ? 0 : i };
        screen.InstallSnapshotRows(storage, null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        return screen;
    }

    private static (ulong Cells, ulong Bytes) Usage(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> group = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) group.Add(row);
        Assert.True(screen.TryGetSnapshotGraphemeUsage(member.SnapshotAllocation!, group, out ulong cells, out ulong bytes));
        return (cells, bytes);
    }

    private static byte[] Snapshot(bool separatePages)
    {
        TerminalScreen screen = new(8, 2);
        SetGrapheme(screen.GetViewportRow(1), 1, 64);
        SetGrapheme(screen.GetViewportRow(1), 3, 64);
        SetGrapheme(screen.GetViewportRow(1), 4, 8);
        using BasicVtProcessor source = new(screen);
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)(separatePages ? 2 : 1));
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                for (int i = 0; i < (separatePages ? 2 : 1); i++)
                {
                    TerminalRow[] rows = separatePages ? [screen.GetRow(i)] : [screen.GetRow(0), screen.GetRow(1)];
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture(rows, screen, 8 * rows.Length).WritePayloadTo(output);
                    byte[] page = output.ToArray();
                    BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(12), 1024);
                    records.Add(new(tag, page));
                }
            }
            else records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
