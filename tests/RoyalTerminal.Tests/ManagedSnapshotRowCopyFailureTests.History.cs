// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedSnapshotRowCopyFailureTests
{
    [Theory]
    [InlineData("grapheme", false)]
    [InlineData("grapheme", true)]
    [InlineData("style", false)]
    [InlineData("style", true)]
    [InlineData("string", false)]
    [InlineData("string", true)]
    [InlineData("map", false)]
    [InlineData("map", true)]
    public void HistoryBoundaryCopyExhaustionFaultsOnlyItsOwnerAndNeverPublishesAHold(string metadata, bool held)
    {
        using BasicVtProcessor processor = HistoryFixture(metadata, true, out TerminalScreen screen);
        TerminalScreen retained = screen.CreateStateCopy();
        if (held) Process(processor, "\u001b[?2026h");
        int callbacks = 0;
        processor.TitleCallback = _ => callbacks++;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Process(processor, "\n\u001b]2;unaccepted\a"));

        Assert.Contains("row metadata copy", failure.Message);
        Assert.Equal(!held, screen.SnapshotMutationFailed);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(0, callbacks);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.Process("X"u8)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.GetBinarySnapshot()));
        Assert.False(processor.RefreshTimedState());
        processor.Dispose();
        Assert.Equal(7, retained.TotalRows);
        Assert.Equal('S', retained.GetViewportRow(3).ReadOnlyCells[0].Codepoint);
        Assert.Equal('D', retained.GetViewportRow(6).ReadOnlyCells[0].Codepoint);
        if (held)
        {
            Assert.Equal(7, screen.TotalRows);
            Assert.Equal('S', screen.GetViewportRow(3).ReadOnlyCells[0].Codepoint);
            Assert.Equal('D', screen.GetViewportRow(6).ReadOnlyCells[0].Codepoint);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryBoundaryGrowthRetriesAfterRotatingAndRemappingPhysicalSlots(bool held)
    {
        using BasicVtProcessor processor = HistoryFixture("grapheme", false, out TerminalScreen screen);
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageAllocation original = screen.GetViewportRow(4).SnapshotAllocation!;
        if (held) Process(processor, "\u001b[?2026h");

        processor.Process("\n"u8);
        if (held) Process(processor, "\u001b[?2026l");

        Assert.False(screen.SnapshotMutationFailed);
        Assert.Equal(8, screen.TotalRows);
        TerminalRow copied = screen.GetViewportRow(3);
        Assert.NotSame(original, copied.SnapshotAllocation);
        Assert.True(copied.SnapshotAllocation!.Capacity.GraphemeBytes > 1024);
        Assert.Equal(0, copied.SnapshotAllocationRow);
        Assert.Equal("S" + new string('\u0301', 4), copied.ReadOnlyCells[0].Grapheme);
        Assert.Equal("S" + new string('\u0301', 64), copied.ReadOnlyCells[1].Grapheme);
        Assert.Same(original, retained.GetViewportRow(4).SnapshotAllocation);
        Assert.Equal('D', retained.GetViewportRow(6).ReadOnlyCells[0].Codepoint);
        Process(processor, "\u001b[4;2H\u0301\u001b[P");
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    private static BasicVtProcessor HistoryFixture(string metadata, bool exhausted, out TerminalScreen screen)
    {
        int columns = metadata == "map" ? 108 : 8;
        TerminalScreen owner = new(columns, 7);
        screen = TerminalScreen.CreateSnapshotStorage(columns, 7, 100, owner.Theme);
        GhosttySnapshotCapacityDimension dimension = metadata switch
        {
            "style" => GhosttySnapshotCapacityDimension.Styles,
            "string" => GhosttySnapshotCapacityDimension.StringBytes,
            "map" => GhosttySnapshotCapacityDimension.HyperlinkBytes,
            _ => GhosttySnapshotCapacityDimension.GraphemeBytes,
        };
        GhosttySnapshotPageAllocation source = new(new((ushort)columns, 4, 16, 192, 1024, 2048));
        GhosttySnapshotPageAllocation target = new(exhausted ? CeilingCapacity(dimension) : new((ushort)columns, 3, 16, 192, 1024, 2048));
        TerminalRow[] rows = new TerminalRow[7];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = new(columns)
            {
                SnapshotAllocation = row < 4 ? source : target,
                SnapshotAllocationRow = row < 4 ? row : row - 4,
            };
        }
        for (int i = 0; i < columns; i++) rows[6][i].Codepoint = 'D';
        rows[3][0].Codepoint = rows[3][1].Codepoint = 'S';
        if (metadata == "grapheme")
        {
            for (int i = 0; i < 4; i++) SetGrapheme(rows[4], i, 'P', i == 3 ? 56 : 64);
            SetGrapheme(rows[3], 0, 'S', 4);
            SetGrapheme(rows[3], 1, 'S', 64);
        }
        else if (metadata == "style")
        {
            rows[4][0].Codepoint = rows[4][1].Codepoint = 'P';
            rows[4][0].Attributes = CellAttributes.Bold;
            rows[4][1].Attributes = CellAttributes.Dim;
            rows[3][0].Attributes = CellAttributes.Bold;
            rows[3][1].Attributes = CellAttributes.Italic;
        }
        else
        {
            int pressure = screen.RegisterHyperlink(Encoding.ASCII.GetBytes(new string('p', metadata == "map" ? 1 : 1984)), default, 1);
            for (int i = 0; i < (metadata == "map" ? 102 : 1); i++)
            {
                rows[4][i].Codepoint = 'P';
                rows[4][i].HyperlinkId = pressure;
            }
            rows[3][0].HyperlinkId = screen.RegisterHyperlink("s"u8, default, 2);
            rows[3][1].HyperlinkId = screen.RegisterHyperlink(Encoding.ASCII.GetBytes(new string('t', 128)), default, 3);
        }
        screen.InstallSnapshotRows(rows, null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[1;2r\u001b[2;1H");
        return processor;
    }
}
