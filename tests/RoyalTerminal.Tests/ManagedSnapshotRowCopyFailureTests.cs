// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.clonePartialRowGrowCapacity panics once growth is exhausted
// because callers already shifted rows/pins. WT TextBuffer/ROW and xterm.js
// BufferLine have no Ghostty PAGE quota. The managed host deliberately contains
// this fatal condition to one screen/processor: no successful copy, recovery
// through VT reset, snapshot export, or publication of a failed output hold.
public sealed class ManagedSnapshotRowCopyFailureTests
{
    [Theory]
    [InlineData("insert", "grapheme")]
    [InlineData("delete", "grapheme")]
    [InlineData("down", "grapheme")]
    [InlineData("up", "grapheme")]
    [InlineData("insert", "style")]
    [InlineData("delete", "style")]
    [InlineData("insert", "string")]
    [InlineData("delete", "string")]
    [InlineData("insert", "map")]
    [InlineData("delete", "map")]
    public void ClearingAnEntireRegionDoesNotAttemptAnUnnecessaryFatalCopy(string operation, string metadata)
    {
        using BasicVtProcessor processor = Fixture(operation, false, true,
            out TerminalScreen screen, out string command, out int destination, metadata);
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageAllocation allocation = screen.GetViewportRow(destination).SnapshotAllocation!;

        Process(processor, command.Insert(2, "65535"));

        Assert.False(screen.SnapshotMutationFailed);
        Assert.Same(allocation, screen.GetViewportRow(destination).SnapshotAllocation);
        foreach (TerminalCell cell in screen.GetViewportRow(destination).ReadOnlyCells)
        {
            Assert.Equal(0, cell.Codepoint);
            Assert.Null(cell.Grapheme);
            Assert.Equal(CellAttributes.None, cell.Attributes);
            Assert.Equal(0, cell.HyperlinkId);
        }
        Assert.Equal('D', retained.GetViewportRow(destination).ReadOnlyCells[0].Codepoint);
        processor.Process("X"u8);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    [Theory]
    [InlineData("insert", false)]
    [InlineData("insert", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("down", false)]
    [InlineData("down", true)]
    [InlineData("reverse", false)]
    [InlineData("reverse", true)]
    [InlineData("up", false)]
    [InlineData("up", true)]
    [InlineData("linefeed", false)]
    [InlineData("linefeed", true)]
    public void ExhaustedRowCopyStopsBeforeCopyingUnacceptedPayload(string operation, bool rectangle)
    {
        using BasicVtProcessor processor = Fixture(operation, rectangle, true,
            out TerminalScreen screen, out string command, out int destination);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow row = screen.GetViewportRow(destination);
        GhosttySnapshotPageAllocation allocation = row.SnapshotAllocation!;
        int callbacks = 0;
        processor.TitleCallback = _ => callbacks++;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Process(processor, command + "\u001b]2;unaccepted\a"));

        Assert.Contains("row metadata copy", failure.Message);
        Assert.True(screen.SnapshotMutationFailed);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(0, callbacks);
        Assert.Same(allocation, row.SnapshotAllocation);
        Assert.False(allocation.MetadataOverflow); // Not a usable overflow fallback.
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal('D', row.ReadOnlyCells[i].Codepoint);
            Assert.Null(row.ReadOnlyCells[i].Grapheme);
            Assert.Equal('D', retained.GetViewportRow(destination).ReadOnlyCells[i].Codepoint);
        }
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.Process("X"u8)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Process(processor, "\u001bc")));
        TerminalScreen copiedFault = screen.CreateStateCopy();
        Assert.True(copiedFault.SnapshotMutationFailed);
        using BasicVtProcessor replacement = new(screen);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => replacement.Process("X"u8)));
        using BasicVtProcessor independent = new(retained);
        independent.Process("X"u8);
        Assert.False(retained.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoverableGrowthRetriesTheCopyWithoutFaulting(bool rectangle)
    {
        using BasicVtProcessor processor = Fixture("insert", rectangle, false,
            out TerminalScreen screen, out string command, out int destination);
        GhosttySnapshotPageAllocation previous = screen.GetViewportRow(destination).SnapshotAllocation!;

        Process(processor, command);

        TerminalRow row = screen.GetViewportRow(destination);
        int start = rectangle ? 1 : 0;
        Assert.False(screen.SnapshotMutationFailed);
        Assert.NotSame(previous, row.SnapshotAllocation);
        Assert.True(row.SnapshotAllocation!.Capacity.GraphemeBytes > 1024);
        Assert.Equal("S" + new string('\u0301', 4), row.ReadOnlyCells[start].Grapheme);
        Assert.Equal("S" + new string('\u0301', 64), row.ReadOnlyCells[start + 1].Grapheme);
        if (rectangle)
        {
            Assert.Equal('D', row.ReadOnlyCells[0].Codepoint);
            Assert.Equal('D', row.ReadOnlyCells[7].Codepoint);
        }
        processor.Process("X"u8);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    [Theory]
    [InlineData("style", false)]
    [InlineData("style", true)]
    [InlineData("string", false)]
    [InlineData("string", true)]
    [InlineData("map", false)]
    [InlineData("map", true)]
    public void ExhaustionOfEveryMetadataKindIsFatal(string metadata, bool rectangle)
    {
        using BasicVtProcessor processor = Fixture("insert", rectangle, true,
            out TerminalScreen screen, out string command, out int destination, metadata);

        Assert.Throws<InvalidOperationException>(() => Process(processor, command));

        Assert.True(screen.SnapshotMutationFailed);
        Assert.False(screen.GetViewportRow(destination).SnapshotAllocation!.MetadataOverflow);
        foreach (TerminalCell cell in screen.GetViewportRow(destination).ReadOnlyCells)
            Assert.Equal('D', cell.Codepoint);
    }

    [Fact]
    public void FaultRejectsReuseAndExportsBeforeWritingAnyOutput()
    {
        using BasicVtProcessor processor = Fixture("insert", false, true,
            out TerminalScreen screen, out string command, out _);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Process(processor, command));
        using MemoryStream bytes = new();
        List<TerminalSearchMatch> matches = [];

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.Process(ReadOnlySpan<byte>.Empty)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.ProcessUntilGround("X"u8, out _)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(processor.Reset));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.PrepareForNewSession(false)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.PrepareForNewSession(true)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(processor.ClearScrollback));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(processor.ClearVisibleHistory));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.ResizeScreen(10, 4, 100, 40, true)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.NotifyResize(10, 4)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.ApplyTheme(screen.Theme)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.GetBinarySnapshot()));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.WriteBinarySnapshotTo(bytes)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.GetContinuation()));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.WriteContinuationTo(bytes)));
        Assert.Equal(0, bytes.Length);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.TryExportSnapshot(
            TerminalSnapshotExportFormat.PlainText, new(), out _)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.TryExportSnapshot(
            TerminalSnapshotExportFormat.StyledVt, new(), out _)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.TryExportSnapshot(
            TerminalSnapshotExportFormat.Html, new(), out _)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.ReadSelection(default)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.PopulateSearchMatches("S", matches)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.PopulateSearchMatchesAsync("S", matches)));
        Assert.Empty(matches);
        Assert.False(processor.RefreshTimedState());
        Assert.Null(processor.NextTimedRefreshDelay);
        processor.CancelSearch();
        processor.Dispose();
        Assert.True(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedHeldMutationNeverPublishesOnRefreshOrDispose(bool rectangle)
    {
        using BasicVtProcessor processor = Fixture("insert", rectangle, true,
            out TerminalScreen screen, out string command, out int destination);
        TerminalRow before = screen.GetViewportRow(destination);
        Process(processor, "\u001b[?2026h");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Process(processor, command));

        Assert.False(screen.SnapshotMutationFailed); // Only the private owner faulted.
        Assert.Same(before, screen.GetViewportRow(destination));
        Assert.False(processor.RefreshTimedState());
        Assert.Null(processor.NextTimedRefreshDelay);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Process(processor, "\u001b[?2026l")));
        processor.Dispose();
        Assert.False(screen.SnapshotMutationFailed);
        Assert.Same(before, screen.GetViewportRow(destination));
        for (int i = 0; i < 8; i++) Assert.Equal('D', before.ReadOnlyCells[i].Codepoint);
    }

    [Fact]
    public void OutputWorkerReportsTheFatalCopyAndDoesNotRunLaterOutput()
    {
        using BasicVtProcessor processor = Fixture("insert", false, true,
            out TerminalScreen screen, out string command, out _);
        int later = 0;
        using TerminalOutputWorker worker = new(() =>
        {
            Process(processor, command);
            Interlocked.Increment(ref later);
            processor.Process("X"u8);
        });
        worker.Schedule();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(worker.Flush);

        Assert.Contains("row metadata copy", failure.Message);
        Assert.True(screen.SnapshotMutationFailed);
        Assert.Equal(0, Volatile.Read(ref later));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(worker.Schedule));
        worker.Dispose();
    }

    private static BasicVtProcessor Fixture(string operation, bool rectangle, bool exhausted,
        out TerminalScreen screen, out string command, out int destination, string metadata = "grapheme")
    {
        bool upward = operation is "up" or "linefeed";
        destination = operation == "delete" ? 0 : upward ? 1 : 2;
        int source = upward ? 2 : 1;
        int pressure = operation == "delete" ? 2 : 0;
        int columns = metadata == "map" ? 108 : 8;
        TerminalScreen owner = new(columns, 3);
        screen = TerminalScreen.CreateSnapshotStorage(columns, 3, 100, owner.Theme);
        GhosttySnapshotCapacityDimension dimension = metadata switch
        {
            "style" => GhosttySnapshotCapacityDimension.Styles,
            "string" => GhosttySnapshotCapacityDimension.StringBytes,
            "map" => GhosttySnapshotCapacityDimension.HyperlinkBytes,
            _ => GhosttySnapshotCapacityDimension.GraphemeBytes,
        };
        GhosttySnapshotPageCapacity capacity = exhausted ? CeilingCapacity(dimension) : new((ushort)columns, 2, 16, 192, 1024, 2048);
        GhosttySnapshotPageAllocation target = new(capacity);
        TerminalRow[] rows = [new(columns), new(columns), new(columns)];
        rows[pressure].SnapshotAllocation = target;
        rows[pressure].SnapshotAllocationRow = 0;
        rows[destination].SnapshotAllocation = target;
        rows[destination].SnapshotAllocationRow = 1;
        rows[source].SnapshotAllocation = new(new((ushort)columns, 1, 16, 192, 1024, 2048));
        for (int i = 0; i < columns; i++) rows[destination][i].Codepoint = 'D';
        int start = rectangle ? 1 : 0;
        if (metadata == "grapheme")
        {
            // Keep 992 bytes live in a different row of the destination page.
            // The first 16-byte suffix clone fits; the next 256-byte clone fails.
            for (int i = 0; i < 4; i++) SetGrapheme(rows[pressure], i, 'P', i == 3 ? 56 : 64);
            SetGrapheme(rows[source], start, 'S', 4);
            SetGrapheme(rows[source], start + 1, 'S', 64);
        }
        else if (metadata == "style")
        {
            rows[pressure][0].Codepoint = rows[pressure][1].Codepoint = 'P';
            rows[pressure][0].Attributes = CellAttributes.Bold;
            rows[pressure][1].Attributes = CellAttributes.Dim;
            rows[source][start].Codepoint = rows[source][start + 1].Codepoint = 'S';
            rows[source][start].Attributes = CellAttributes.Bold;
            rows[source][start + 1].Attributes = CellAttributes.Italic;
        }
        else
        {
            int existing = screen.RegisterHyperlink(Encoding.ASCII.GetBytes(new string('p', metadata == "map" ? 1 : 1984)), default, 1);
            for (int i = 0; i < (metadata == "map" ? 102 : 1); i++)
            {
                rows[pressure][i].Codepoint = 'P';
                rows[pressure][i].HyperlinkId = existing;
            }
            rows[source][start].Codepoint = rows[source][start + 1].Codepoint = 'S';
            rows[source][start].HyperlinkId = screen.RegisterHyperlink("s"u8, default, 2);
            rows[source][start + 1].HyperlinkId = screen.RegisterHyperlink(Encoding.ASCII.GetBytes(new string('t', 128)), default, 3);
        }
        screen.InstallSnapshotRows(rows, null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        BasicVtProcessor processor = new(screen);
        string setup = rectangle ? $"\u001b[?69h\u001b[2;{columns - 1}s" : "";
        if (upward) setup += "\u001b[2;3r";
        setup += operation == "linefeed" ? $"\u001b[3;{start + 1}H" : $"\u001b[1;{start + 1}H";
        Process(processor, setup);
        command = operation switch
        {
            "insert" => "\u001b[L", "delete" => "\u001b[M", "down" => "\u001b[T",
            "reverse" => "\u001bM", "up" => "\u001b[S", "linefeed" => "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return processor;
    }

    private static GhosttySnapshotPageCapacity CeilingCapacity(GhosttySnapshotCapacityDimension dimension)
    {
        GhosttySnapshotAllocation layout = new(4096);
        for (int columns = ushort.MaxValue; columns >= 32768; columns--)
        {
            int rows = (int)(uint.MaxValue / (8UL * ((ulong)columns + 1)));
            GhosttySnapshotPageCapacity capacity = new((ushort)columns, (ushort)rows,
                dimension == GhosttySnapshotCapacityDimension.Styles ? (ushort)4 : (ushort)16, 192, 1024, 2048);
            while (layout.LayoutBytes(capacity) > uint.MaxValue) capacity = capacity with { Rows = (ushort)(capacity.Rows - 1) };
            if (!layout.TryIncreaseCapacity(capacity, dimension, dimension == GhosttySnapshotCapacityDimension.GraphemeBytes ? 1008UL : 0, 2, out _)) return capacity;
        }
        throw new InvalidOperationException("No representable growth-limited PAGE fixture was found.");
    }

    private static void SetGrapheme(TerminalRow row, int column, char codepoint, int suffix)
    {
        row[column].Codepoint = codepoint;
        row[column].Grapheme = codepoint + new string('\u0301', suffix);
    }

    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));
}
