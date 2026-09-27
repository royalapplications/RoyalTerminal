// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageList.ReflowCursor.writeCell/hyperlinkStringsFit is the allocator
// oracle: reflow probes strings before deduplication and rechecks after page
// replacement. WT TextBuffer::Reflow and xterm.js Buffer._reflow preserve text
// with different row/line stores, not Ghostty's page-local capacity contract.
public sealed class ManagedSnapshotReflowMetadataTests
{
    [Theory]
    [InlineData(false, 4096)]
    [InlineData(true, 4096)]
    [InlineData(false, 16384)]
    [InlineData(true, 16384)]
    public void DuplicateReflowLinksGrowStringsWithoutChangingContentOrCowOwners(bool explicitId, int alignment)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(explicitId));
        terminal.Screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        TerminalRow original = retained.GetSnapshotRows(0)![0];
        Assert.Equal(2048U, original.SnapshotAllocation!.Capacity.StringBytes);

        terminal.Processor.ResizeScreen(16, 2, 0, 0);

        TerminalRow row = terminal.Screen.GetSnapshotRows(0)![0];
        Assert.Equal(4096U, row.SnapshotAllocation!.Capacity.StringBytes);
        Assert.Equal((ushort)192, row.SnapshotAllocation.Capacity.HyperlinkBytes);
        Assert.False(row.SnapshotAllocation.MetadataOverflow);
        for (int column = 0; column < 8; column++)
        {
            TerminalCell actual = row.ReadOnlyCells[column], expected = original.ReadOnlyCells[column];
            Assert.Equal(expected.Codepoint, actual.Codepoint);
            Assert.Equal(expected.Grapheme, actual.Grapheme);
            Assert.Equal(expected.Attributes, actual.Attributes);
            Assert.NotEqual(0, actual.HyperlinkId);
            Assert.Equal(row.ReadOnlyCells[0].HyperlinkId, actual.HyperlinkId);
        }
        Assert.Equal(8, retained.Columns);
        Assert.Equal(2048U, original.SnapshotAllocation.Capacity.StringBytes);
        Assert.Equal((1UL, 8UL, explicitId ? 2016UL : 2048UL), Usage(terminal.Screen, row));
        Assert.Equal((1UL, 8UL, explicitId ? 2016UL : 2048UL), Usage(retained, original));

        terminal.Processor.Process("\u001b[1;1H\u001b[2K"u8);
        Assert.Equal(0, row.ReadOnlyCells[0].HyperlinkId);
        Assert.NotEqual(0, original.ReadOnlyCells[0].HyperlinkId);
        Assert.Equal((1UL, 8UL, explicitId ? 2016UL : 2048UL), Usage(retained, original));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedMetadataPressureMatchesNativeAcrossRepeatedColumnResizes(bool explicitId)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
            Assert.Skip("Native VT library is unavailable.");
        }
        byte[] snapshot = Snapshot(explicitId);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        native.SetResizePullScrollback(false);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot, new()
        {
            ProcessorOptions = new() { ResizePullScrollback = false },
        });
        managed.Screen.SnapshotScrollbackQuota = new()
        {
            PageAlignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096,
        };
        foreach (ushort columns in new ushort[] { 16, 3, 9, 4, 16 })
        {
            native.Resize(columns, 2);
            managed.Processor.ResizeScreen(columns, 2, 0, 0);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
            TerminalRowBuffer rows = managed.Screen.GetSnapshotRows(0)!;
            int nativeRows = 0;
            foreach (GhosttySnapshotPage page in expected.Pages) nativeRows += page.Grid.Rows;
            int index = rows.Count - nativeRows;
            Assert.True(index >= 0);
            TerminalScreen reference = new(columns, 2);
            foreach (GhosttySnapshotPage page in expected.Pages)
            foreach (TerminalRow expectedRow in GhosttySnapshotLivePage.Decode(page, reference))
            {
                TerminalRow actualRow = rows[index++];
                GhosttySnapshotPageCapacity capacity = actualRow.SnapshotAllocation!.Capacity;
                // Ghostty snapshot/page.zig Header.init writes size.rows, not
                // capacity.rows. Compare the wire's logical page extent and
                // all four metadata hints; reserved rows are not serialized.
                int logicalRows = 0;
                foreach (TerminalRow member in rows)
                    if (ReferenceEquals(member.SnapshotAllocation, actualRow.SnapshotAllocation)) logicalRows++;
                Assert.InRange(logicalRows, 1, capacity.Rows);
                Assert.Equal(page.Capacity, capacity with { Rows = checked((ushort)logicalRows) });
                Assert.False(actualRow.SnapshotAllocation.MetadataOverflow);
                for (int column = 0; column < columns; column++)
                {
                    TerminalCell a = actualRow.ReadOnlyCells[column], b = expectedRow.ReadOnlyCells[column];
                    Assert.Equal(b.Codepoint, a.Codepoint);
                    Assert.Equal(b.Grapheme, a.Grapheme);
                    Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in b), GhosttySnapshotLivePage.EncodeStyle(in a));
                    Assert.Equal(reference.TryGetHyperlink(b.HyperlinkId, out TerminalHyperlink? expectedLink),
                        managed.Screen.TryGetHyperlink(a.HyperlinkId, out TerminalHyperlink? actualLink));
                    if (expectedLink is not null)
                        Assert.Equal(expectedLink.SnapshotEncoding, actualLink!.SnapshotEncoding);
                }
            }
        }
    }

    private static (ulong Links, ulong Cells, ulong Bytes) Usage(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> group = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) group.Add(row);
        Assert.True(screen.TryGetSnapshotHyperlinkUsage(member.SnapshotAllocation!, group, out ulong links, out ulong cells, out ulong bytes));
        return (links, cells, bytes);
    }

    private static byte[] Snapshot(bool explicitId)
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 2));
        string uri = new('u', explicitId ? 1984 : 2048);
        source.Process(Encoding.UTF8.GetBytes($"\u001b[1m\u001b]8;{(explicitId ? "id=i" : "")};{uri}\aA\u0301B\u0301C\u0301D\u0301E\u0301F\u0301G\u0301H\u0301\u001b]8;;\a\u001b[0m"));
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 1_000_000);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 16);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), 192);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1024);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 2048);
            }
            records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }
}
