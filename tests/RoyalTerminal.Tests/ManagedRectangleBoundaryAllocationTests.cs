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

// Ghostty Terminal.rowWillBeShifted runs on each destination then source, not
// on the entire rectangle before cloning. It drops graphemes but keeps styles
// and links at split boundaries. WT bounded rectangle scrolling and xterm.js
// line splices do not define the PAGE allocation order; use Ghostty's contract.
public sealed class ManagedRectangleBoundaryAllocationTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public void UnvisitedBoundarySuffixKeepsItsAllocationDuringCrossPageCopy(bool down, bool rightEdge, bool held)
    {
        byte[] snapshot = Snapshot(down, rightEdge);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        int destination = down ? 2 : 0;
        int outside = rightEdge ? 6 : 0;
        GhosttySnapshotPageAllocation original = managed.Screen.GetViewportRow(destination).SnapshotAllocation!;
        string command = Setup(rightEdge) + (held ? "\u001b[?2026h" : "") + (down ? "\u001b[2L" : "\u001b[2M");
        managed.Processor.Process(Encoding.UTF8.GetBytes(command));
        if (held)
        {
            Assert.Same(original, managed.Screen.GetViewportRow(destination).SnapshotAllocation);
            managed.Processor.Process("\u001b[?2026l"u8);
        }
        TerminalRow row = managed.Screen.GetViewportRow(destination);
        Assert.Equal(2048U, row.SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal(1024U, retained.GetViewportRow(destination).SnapshotAllocation!.Capacity.GraphemeBytes);
        Assert.Equal("中" + new string('\u0301', 64), retained.GetViewportRow(1).ReadOnlyCells[outside].Grapheme);
        Assert.Null(managed.Screen.GetViewportRow(1).ReadOnlyCells[outside].Grapheme);
        // Boundary cleanup is not an erase: the outside style/link survives.
        int survivingOutside = rightEdge ? 7 : 0;
        TerminalCell survivor = managed.Screen.GetViewportRow(1).ReadOnlyCells[survivingOutside];
        Assert.Equal(CellAttributes.Bold, survivor.Attributes);
        Assert.NotEqual(0, survivor.HyperlinkId);
        for (int column = 2; column < 6; column++) Assert.Equal("S" + new string('\u0302', 64), row.ReadOnlyCells[column].Grapheme);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void BoundaryTraversalGrowthAndCellsMatchNative(bool down, bool rightEdge)
    {
        RequireNative();
        byte[] snapshot = Snapshot(down, rightEdge);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        byte[] command = Encoding.UTF8.GetBytes(Setup(rightEdge) + (down ? "\u001b[2L" : "\u001b[2M"));
        native.Write(command);
        managed.Processor.Process(command);
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
        TerminalScreen owner = new(8, 3);
        int index = 0;
        foreach (GhosttySnapshotPage page in expected.Pages)
        foreach (TerminalRow row in GhosttySnapshotLivePage.Decode(page, owner))
        {
            TerminalRow actual = managed.Screen.GetViewportRow(index++);
            Assert.Equal(page.Capacity, actual.SnapshotAllocation!.Capacity);
            for (int column = 0; column < 8; column++)
            {
                TerminalCell cell = row.ReadOnlyCells[column], observed = actual.ReadOnlyCells[column];
                Assert.Equal((cell.Codepoint, cell.Width, cell.Grapheme), (observed.Codepoint, observed.Width, observed.Grapheme));
                Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in cell), GhosttySnapshotLivePage.EncodeStyle(in observed));
                Assert.Equal(cell.HyperlinkId == 0, observed.HyperlinkId == 0);
            }
        }
        Assert.Equal(3, index);
    }

    private static string Setup(bool rightEdge) => "\u001b[?69h" + (rightEdge ? "\u001b[2;7s" : "\u001b[2;8s") + "\u001b[1;2H";

    private static byte[] Snapshot(bool down, bool rightEdge)
    {
        TerminalScreen owner = new(8, 3);
        TerminalRow source = new(8), pressure = new(8), destination = new(8);
        for (int column = 2; column < 6; column++)
        {
            source[column].Codepoint = 'S';
            source[column].Grapheme = "S" + new string('\u0302', 64);
        }
        int boundary = rightEdge ? 6 : 0;
        int link = owner.RegisterHyperlink("https://boundary"u8, "edge"u8, 0);
        pressure[boundary].Codepoint = '中';
        pressure[boundary].Grapheme = "中" + new string('\u0301', 64);
        pressure[boundary].Width = 2;
        pressure[boundary + 1].Width = 0;
        pressure[boundary].Attributes = pressure[boundary + 1].Attributes = CellAttributes.Bold;
        pressure[boundary].HyperlinkId = pressure[boundary + 1].HyperlinkId = link;
        TerminalRow[][] pages = down ? [[source], [pressure, destination]] : [[destination, pressure], [source]];
        using BasicVtProcessor empty = new(owner);
        using GhosttySnapshotRecordReader reader = new(empty.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 2);
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                foreach (TerminalRow[] rows in pages)
                {
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture(rows, owner, rows.Length * 8).WritePayloadTo(output);
                    byte[] page = output.ToArray();
                    // Decode appends scalars with replacement scratch; the
                    // source needs spare space to admit all four suffixes.
                    BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(12), rows.Length == 1 ? 2048U : 1024U);
                    records.Add(new(tag, page));
                }
            }
            else records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
