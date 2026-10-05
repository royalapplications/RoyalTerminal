// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Tabstops/stream.zig define the bitmap, strict CTC subset and resize
// reset policy. WT uses a column vector and xterm.js a sparse object: their
// next/previous edge fallbacks agree, but default endpoint bits and omitted
// DECST8C parameters differ. Match the pinned native engine, not a union.
public sealed class ManagedTabStopParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001b[W")]
    [InlineData("\u001b[0W")]
    [InlineData("\u001bH")]
    public void SetClearAndDefaultResetAreIdempotent(string set)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(521, 2));
        Write(processor, "\u001b[5W\u001b[513G" + set + set);
        Assert.True(processor.IsSnapshotTabStop(512));
        Write(processor, "\u001b[2W\u001b[2W");
        Assert.False(processor.IsSnapshotTabStop(512));
        Write(processor, set + "\u001b[0g\u001b[0g");
        Assert.False(processor.IsSnapshotTabStop(512));
        Write(processor, set + "\u001b[5W\u001b[H\t");
        Assert.Equal(520, processor.CursorCol);
        Write(processor, "\u001b[?5W\u001b[H\t");
        Assert.Equal(8, processor.CursorCol);
        Assert.True(processor.IsSnapshotTabStop(512));
        Assert.False(processor.IsSnapshotTabStop(0));
        Assert.False(processor.IsSnapshotTabStop(520));
    }

    [Theory]
    [InlineData("\u001b[1W")]
    [InlineData("\u001b[3W")]
    [InlineData("\u001b[0;2W")]
    [InlineData("\u001b[2:0W")]
    [InlineData("\u001b[?W")]
    [InlineData("\u001b[?0W")]
    [InlineData("\u001b[?5;5W")]
    [InlineData("\u001b[?5 W")]
    [InlineData("\u001b[>5W")]
    public void UnsupportedControlShapesLeaveStopsUnchanged(string control)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(17, 2));
        Write(processor, "\u001b[3g\u001b[4G\u001bH");
        Write(processor, control);
        for (int column = 0; column < 17; column++)
            Assert.Equal(column == 3, processor.IsSnapshotTabStop(column));
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\u001b[I")]
    [InlineData("\u001b[Z")]
    [InlineData("\u001b[0g")]
    [InlineData("\u001b[3g")]
    [InlineData("\u001b[0W")]
    [InlineData("\u001b[2W")]
    [InlineData("\u001b[5W")]
    [InlineData("\u001b[?5W")]
    [InlineData("\u001bH")]
    public void TabControlsPreservePendingWrapLikeGhostty(string control)
    {
        TerminalScreen screen = new(9, 2);
        using BasicVtProcessor processor = new(screen);
        Write(processor, "123456789" + control + "X");
        Assert.Equal(1, processor.CursorRow);
        Assert.Equal('X', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyTerminal native = new(9, 2);
        native.Write(Encoding.UTF8.GetBytes("123456789" + control + "X"));
        using ManagedTerminalSnapshot expected = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        Assert.Equal(expected.Processor.CursorCol, processor.CursorCol);
        Assert.Equal(expected.Processor.CursorRow, processor.CursorRow);
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 9; column++)
                Assert.Equal(expected.Screen.GetViewportRow(row).ReadOnlyCells[column].Codepoint,
                    screen.GetViewportRow(row).ReadOnlyCells[column].Codepoint);
    }

    [Fact]
    public void NavigationHonorsMarginsAndStopsAtEdgesForLargeCounts()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(521, 2));
        Write(processor, "\u001b[3g\u001b[65G\u001bH\u001b[513G\u001bH\u001b[H\t");
        Assert.Equal(64, processor.CursorCol);
        Write(processor, "\u001b[9999I");
        Assert.Equal(520, processor.CursorCol);
        Write(processor, "\u001b[Z");
        Assert.Equal(512, processor.CursorCol);
        Write(processor, "\u001b[9999Z");
        Assert.Equal(0, processor.CursorCol);
        Write(processor, "\u001b[?69h\u001b[61;501s\u001b[?6h\u001b[9999I");
        Assert.Equal(500, processor.CursorCol);
        Write(processor, "\u001b[9999Z");
        Assert.Equal(60, processor.CursorCol);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(513)]
    public void ResizeRollbackAndBothSnapshotFormatsRetainCustomStops(int columns)
    {
        using BasicVtProcessor source = new(new TerminalScreen(columns, 2));
        Write(source, $"\u001b[3g\u001bH\u001b[{columns}G\u001bH\u001b[65G\u001bH");
        source.ResizeCheckpoint = checkpoint =>
        {
            if (checkpoint == ManagedResizeCheckpoint.Ready) throw new OutOfMemoryException("After staged tab reset");
        };
        byte[] before = source.GetBinarySnapshot();
        Assert.Throws<OutOfMemoryException>(() => source.ResizeScreen(columns + 8, 2, 0, 0, false));
        source.ResizeCheckpoint = null;
        Assert.Equal(before, source.GetBinarySnapshot());
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(before);
        CompareStops(source, restored.Processor, columns);
        using BasicVtProcessor replay = new(new TerminalScreen(columns, 2));
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeTabstops: true)), out string styled));
        Write(replay, styled);
        CompareStops(source, replay, columns);
        source.ResizeScreen(columns, 3, 0, 0, false);
        CompareStops(source, restored.Processor, columns);
        source.ResizeScreen(columns + 8, 3, 0, 0, false);
        for (int c = 0; c < columns + 8; c++)
            Assert.Equal(c > 0 && c < columns + 7 && c % 8 == 0, source.IsSnapshotTabStop(c));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(65)]
    [InlineData(513)]
    [InlineData(521)]
    public void NativeAndManagedExposeIdenticalTabStateAndNavigation(int columns)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native tabstop differential available: {available}");
        if (!available) return;
        using GhosttyTerminal native = new((ushort)columns, 2);
        using BasicVtProcessor managed = new(new TerminalScreen(columns, 2));
        string[] commands = ["", "\u001b[3g", "\u001b[4G\u001b[W", "\u001b[2W\u001b[2W", "\u001b[0W",
            "\u001b[H\t", "\u001b[9999I", "\u001b[Z", "\u001b[9999Z", "\u001b[5W", "\u001b[?5W",
            "\u001b[?W", "\u001b[?69h\u001b[2;7s\u001b[?6h\u001b[9999I\u001b[9999Z"];
        foreach (string command in commands)
        {
            native.Write(Encoding.UTF8.GetBytes(command));
            Write(managed, command);
            using ManagedTerminalSnapshot expected = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
            CompareStops(expected.Processor, managed, columns);
            Assert.Equal(expected.Processor.CursorCol, managed.CursorCol);
            Assert.Equal(expected.Processor.CursorRow, managed.CursorRow);
        }
    }

    private static void CompareStops(BasicVtProcessor expected, BasicVtProcessor actual, int columns)
    {
        for (int c = 0; c < columns; c++) Assert.Equal(expected.IsSnapshotTabStop(c), actual.IsSnapshotTabStop(c));
    }

    private static void Write(BasicVtProcessor processor, string value) => processor.Process(Encoding.UTF8.GetBytes(value));
}
