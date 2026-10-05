// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageList.resizeWithoutReflowGrowCols retries a shorter page after a
// clone failure. A source table can be valid while row-major reinsertion reaches
// the probe bound: an empty-page retry must fail instead of allocating forever.
// WT ResizeTraditional prepares replacement storage before commit; xterm.js
// Buffer.resize has per-line storage and no corresponding PAGE probe limit.
public sealed class ManagedSnapshotColumnResizeFailureTests
{
    [Theory]
    [InlineData(0, 4096, false)]
    [InlineData(1, 4096, false)]
    [InlineData(3, 4096, false)]
    [InlineData(0, 16384, false)]
    [InlineData(1, 16384, false)]
    [InlineData(3, 16384, false)]
    [InlineData(0, 4096, true)]
    [InlineData(1, 4096, true)]
    [InlineData(3, 4096, true)]
    [InlineData(0, 16384, true)]
    [InlineData(1, 16384, true)]
    [InlineData(3, 16384, true)]
    public void NoProgressRejectsResizeAndPreservesBothOwnersAndAnchors(int prefixRows, int alignment, bool held)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(prefixRows));
        TerminalScreen screen = terminal.Screen;
        BasicVtProcessor processor = terminal.Processor;
        screen.SnapshotScrollbackQuota = new() { PageAlignment = alignment };
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow row = screen.GetSnapshotRows(0)![prefixRows];
        GhosttySnapshotPageAllocation allocation = row.SnapshotAllocation!;
        Assert.Equal(33, Styles(screen));
        TerminalScreenAnchor anchor = screen.CreateAnchor(prefixRows, 32);
        TerminalGridPosition[] positions = [new(0, 0), new(32, prefixRows)];
        if (held) processor.Process("\u001b[?2026h"u8);
        byte[] before = processor.GetBinarySnapshot();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            processor.ResizeScreen(128, prefixRows + 1, 0, 0, false, positions));

        Assert.Contains("made no progress", failure.Message);
        Assert.Equal(before, processor.GetBinarySnapshot());
        Assert.Equal(new[] { new TerminalGridPosition(0, 0), new TerminalGridPosition(32, prefixRows) }, positions);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new TerminalGridPosition(32, prefixRows), position);
        Assert.Same(row, screen.GetSnapshotRows(0)![prefixRows]);
        Assert.Same(allocation, row.SnapshotAllocation);
        Assert.False(allocation.MetadataOverflow);
        Assert.False(screen.SnapshotMutationFailed);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(40, screen.Columns);
        Assert.Equal(33, Styles(retained));
        Assert.Equal(CellAttributes.Bold, row.ReadOnlyCells[32].Attributes);

        // The rejected resize is not a permanent mutation fault. Once the
        // offending row is erased, resizing can succeed and retained data stays.
        processor.Process(System.Text.Encoding.ASCII.GetBytes($"\u001b[?2026l\u001b[{prefixRows + 1};1H\u001b[2KZ"));
        processor.ResizeScreen(128, prefixRows + 1, 0, 0, reflowOnResize: false);
        Assert.Equal(128, screen.Columns);
        Assert.Equal('Z', screen.GetSnapshotRows(0)![prefixRows].ReadOnlyCells[0].Codepoint);
        Assert.Equal(CellAttributes.Bold, retained.GetSnapshotRows(0)![prefixRows].ReadOnlyCells[32].Attributes);
        Assert.Equal(40, retained.Columns);
        screen.ReleaseAnchor(anchor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void NativeNoProgressReturnsFailureAndRestoresPinsBeforeRetiringCopies(int prefixRows)
    {
        // Run against a rebuilt extension: the old native library loops on this
        // fixture. CI builds native assets before the managed/native matrix.
        RequireNative();
        using GhosttyTerminal native = GhosttySnapshot.Decode(Snapshot(prefixRows));
        native.Write("\u001b[?7l\u001b[1;1H\u001b7"u8);
        byte[] before = GhosttySnapshot.Encode(native);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => native.Resize(128, (ushort)(prefixRows + 1)));

        Assert.Contains("OutOfMemory", failure.Message);
        Assert.Equal(before, GhosttySnapshot.Encode(native));
        // Saving on an earlier copied row exercises pin rollback when one or
        // more replacement pages had succeeded before the no-progress row.
        native.Write("\u001b8Z"u8);
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotScreen screen = reader.ReadReady().Screens[0];
        Assert.Equal('Z', (int)((screen.Pages[0].Grid.Cells[0] >> 2) & 0xFFFFFF));
    }

    private static int Styles(TerminalScreen screen)
    {
        TerminalRowBuffer rows = screen.GetSnapshotRows(0)!;
        List<TerminalRow> group = [];
        foreach (TerminalRow row in rows) group.Add(row);
        using (screen.EditSnapshotRowMetadata(rows[0])) { }
        Assert.True(screen.TryGetSnapshotStyleUsage(rows[0].SnapshotAllocation!, group, out int styles));
        return styles;
    }

    private static byte[] Snapshot(int prefixRows)
    {
        GhosttySnapshotStyle outsideChain = new(default, default, default, 1);
        Dictionary<ushort, GhosttySnapshotStyle> styles = new() { [1] = outsideChain };
        ulong bucket = (GhosttySnapshotMetadataHash.Style(outsideChain) + 1) & 63;
        for (int rgb = 1; rgb < 65536 && styles.Count < 33; rgb++)
        {
            GhosttySnapshotStyle style = new(new(2, (byte)rgb, (byte)(rgb >> 8), 0), default, default, 0);
            if ((GhosttySnapshotMetadataHash.Style(style) & 63) == bucket)
                styles.Add((ushort)(styles.Count + 1), style);
        }
        Assert.Equal(33, styles.Count);
        ulong[] cells = new ulong[40 * (prefixRows + 1)];
        for (int row = 0; row < prefixRows; row++) cells[row * 40] = (ulong)'P' << 2;
        for (int column = 0; column < 32; column++)
            cells[prefixRows * 40 + column] = ((ulong)'A' << 2) | ((ulong)(column + 2) << 26);
        cells[prefixRows * 40 + 32] = ((ulong)'B' << 2) | (1UL << 26);
        GhosttySnapshotGrid grid = GhosttySnapshotGrid.FromOwnedCells(40, new byte[prefixRows + 1], cells, []);
        GhosttySnapshotPage page = GhosttySnapshotPage.FromOwnedGrid(grid, styles, []);
        using MemoryStream output = new();
        page.WritePayloadTo(output);
        byte[] payload = output.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), 64);

        using BasicVtProcessor template = new(new TerminalScreen(40, prefixRows + 1));
        template.Process("\u001b[?7l"u8);
        using GhosttySnapshotRecordReader reader = new(template.GetBinarySnapshot(), 1_000_000);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> data);
            records.Add(new(tag, tag == GhosttySnapshotRecordTag.Page ? payload : data.ToArray()));
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
