// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Terminal.insertBlanks/deleteChars/eraseChars and
// Screen.splitCellBoundary define cleanup BEFORE movement, including metadata
// release and the current erase background. WT _InsertDeleteCharacterHelper
// uses a bounded rectangle; xterm.js BufferLine also clears split wide cells,
// but repairs some edges after copying and has no Ghostty PAGE allocator.
// Follow Ghostty's order, margins and wrapped-history boundary semantics.
public sealed class ManagedCharacterEditParityTests
{
    private const string Close = "\u001b]8;;\u001b\\";
    private const string Seed = "\u001b[1;41m\u001b]8;id=a;https://a\u001b\\中\u0301" + Close +
        "\u001b[3;42m\u001b]8;id=b;https://b\u001b\\文\u0302" + Close + "\u001b[0mABCD";

    [Theory]
    [InlineData('@', false)]
    [InlineData('@', true)]
    [InlineData('P', false)]
    [InlineData('P', true)]
    [InlineData('X', false)]
    [InlineData('X', true)]
    public void CharacterEditBoundariesMatchNativeForEveryColumnAndCount(char operation, bool margins)
    {
        RequireNative();
        // Alternate layout puts a wide head exactly on the right margin.
        foreach (string contents in new[] { Seed, "A" + Seed, "ABCD" + Seed })
        {
            string setup = contents + (margins ? "\u001b[?69h\u001b[2;7s" : "") + "\u001b[0;44m";
            using GhosttyTerminal source = new(8, 3);
            source.Write(Encoding.UTF8.GetBytes(setup));
            byte[] snapshot = GhosttySnapshot.Encode(source);
            for (int column = 0; column < 8; column++)
            for (int count = 1; count <= 8; count++)
            {
                using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
                using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
                TerminalScreen retained = managed.Screen.CreateStateCopy();
                TerminalCell[] retainedCells = retained.GetViewportRow(0).ReadOnlyCells.ToArray();
                string context = $"{operation}, margins={margins}, prefix={contents.IndexOf('\u001b')}, column={column}, count={count}";
                byte[] command = Encoding.UTF8.GetBytes($"\u001b[1;{column + 1}H\u001b[{count}{operation}");
                native.Write(command);
                managed.Processor.Process(command);
                AssertNative(native, managed, context);
                Assert.Equal(retainedCells, retained.GetViewportRow(0).ReadOnlyCells.ToArray());
                // Reuse metadata after deletion and copying, including growing
                // a suffix and inserting a new explicit link at the edit point.
                byte[] follow = Encoding.UTF8.GetBytes("\u001b[4m\u001b]8;id=c;https://c\u001b\\Z\u0303" + Close);
                native.Write(follow);
                managed.Processor.Process(follow);
                AssertNative(native, managed, context + ", subsequent write");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeletionCannotJoinUnrelatedWideHalvesOrRetainTheirMetadata(bool restored, bool held)
    {
        TerminalScreen initial = new(8, 3) { SnapshotScrollbackQuota = new() };
        using BasicVtProcessor live = new(initial);
        live.Process(Encoding.UTF8.GetBytes(Seed));
        using ManagedTerminalSnapshot? copy = restored ? ManagedTerminalSnapshot.Restore(live.GetBinarySnapshot()) : null;
        BasicVtProcessor processor = copy?.Processor ?? live;
        TerminalScreen screen = copy?.Screen ?? initial;
        TerminalScreen retained = screen.CreateStateCopy();
        if (held) processor.Process("\u001b[?2026h"u8);
        processor.Process("\u001b[0;44m\u001b[1;2H\u001b[2P"u8);
        if (held)
        {
            Assert.Equal(0x4E2D, screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
            processor.Process("\u001b[?2026l"u8);
        }
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal("\0\0ABCD\0\0", new string(row.ReadOnlyCells.ToArray().Select(c => (char)c.Codepoint).ToArray()));
        for (int column = 0; column < 2; column++)
        {
            TerminalCell cell = row.ReadOnlyCells[column];
            Assert.Equal((byte)1, cell.Width);
            Assert.Equal(TerminalColorIdentity.Palette(4), cell.BackgroundIdentity);
            Assert.Equal(0, cell.HyperlinkId);
            Assert.Null(cell.Grapheme);
            Assert.Equal(CellAttributes.None, cell.Attributes);
        }
        List<TerminalRow> group = Group(screen, row);
        Assert.True(screen.TryGetSnapshotGraphemeUsage(row.SnapshotAllocation!, group, out ulong graphemes, out ulong bytes));
        Assert.Equal((0UL, 0UL), (graphemes, bytes));
        Assert.True(screen.TryGetSnapshotHyperlinkUsage(row.SnapshotAllocation!, group, out ulong links, out ulong linkedCells, out _));
        Assert.Equal((0UL, 0UL), (links, linkedCells));
        Assert.True(screen.TryGetSnapshotStyleUsage(row.SnapshotAllocation!, group, out int styles));
        Assert.Equal(1, styles); // Only the current blue-background pen remains.
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal("中\u0301", retained.GetViewportRow(0).ReadOnlyCells[0].Grapheme);
        Assert.Equal("文\u0302", retained.GetViewportRow(0).ReadOnlyCells[2].Grapheme);
    }

    [Theory]
    [InlineData('P', false)]
    [InlineData('P', true)]
    [InlineData('X', false)]
    [InlineData('X', true)]
    public void WrappedWideBoundaryClearsThePreviousHistoryRow(char operation, bool isoProtection)
    {
        RequireNative();
        using GhosttyTerminal native = new(8, 2, 1024 * 1024);
        native.Write(Encoding.UTF8.GetBytes("abcdefg" + (isoProtection ? "\u001bV" : "") + "中\u0301" +
            (isoProtection ? "\u001bW" : "") + "\r\n\u001b[1;2H\u001b[44m"));
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        Assert.True(retained.GetRow(0).ReadOnlyCells[7].IsWideSpacerHead);
        Assert.True(retained.GetViewportRow(0).IsWrapContinuation);
        byte[] command = Encoding.UTF8.GetBytes($"\u001b[{operation}");
        native.Write(command);
        managed.Processor.Process(command);
        AssertNative(native, managed, $"history {operation}, protected={isoProtection}");
        Assert.False(managed.Screen.GetRow(0).ReadOnlyCells[7].IsWideSpacerHead);
        Assert.Equal(TerminalColorIdentity.Palette(4), managed.Screen.GetRow(0).ReadOnlyCells[7].BackgroundIdentity);
        Assert.True(retained.GetRow(0).ReadOnlyCells[7].IsWideSpacerHead);
    }

    [Theory]
    [InlineData('@')]
    [InlineData('P')]
    [InlineData('X')]
    public void OutOfMarginEditPreservesNativePendingWrapPolicy(char operation)
    {
        RequireNative();
        using GhosttyTerminal native = new(8, 3);
        native.Write("\u001b[?69h\u001b[2;6s\u001b[1;8HA"u8);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        byte[] command = Encoding.UTF8.GetBytes($"\u001b[{operation}");
        native.Write(command);
        managed.Processor.Process(command);
        AssertNative(native, managed, "outside margin " + operation);
        native.Write("B"u8);
        managed.Processor.Process("B"u8);
        AssertNative(native, managed, "outside margin after print " + operation);
    }

    private static void AssertNative(GhosttyTerminal native, ManagedTerminalSnapshot managed, string context)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
        using GhosttySnapshotStateReader actualReader = new(managed.Processor.GetBinarySnapshot(), new());
        GhosttySnapshotScreen actual = actualReader.ReadReady().Screens[0];
        Assert.Equal((expected.State.CursorX, expected.State.CursorY, expected.State.PendingWrap),
            (actual.State.CursorX, actual.State.CursorY, actual.State.PendingWrap));
        TerminalScreen owner = new(8, 3);
        int rowIndex = 0;
        foreach (GhosttySnapshotPage page in expected.Pages)
        foreach (TerminalRow row in GhosttySnapshotLivePage.Decode(page, owner))
        {
            TerminalRow observed = managed.Screen.GetRow(rowIndex++);
            Assert.Equal(page.Capacity, observed.SnapshotAllocation!.Capacity);
            Assert.False(observed.SnapshotAllocation.MetadataOverflow);
            Assert.Equal((row.WrapsToNext, row.IsWrapContinuation), (observed.WrapsToNext, observed.IsWrapContinuation));
            for (int column = 0; column < row.Columns; column++)
            {
                TerminalCell left = row.ReadOnlyCells[column], right = observed.ReadOnlyCells[column];
                Assert.True((left.Codepoint, left.Width, left.Grapheme, left.IsWideSpacerHead, left.IsProtected) ==
                    (right.Codepoint, right.Width, right.Grapheme, right.IsWideSpacerHead, right.IsProtected),
                    $"{context}, row={rowIndex - 1}, col={column}: expected {left.Codepoint}/{left.Width}, actual {right.Codepoint}/{right.Width}");
                Assert.True(GhosttySnapshotLivePage.EncodeStyle(in left) == GhosttySnapshotLivePage.EncodeStyle(in right),
                    $"{context}, row={rowIndex - 1}, col={column}: style differs");
                owner.TryGetHyperlink(left.HyperlinkId, out TerminalHyperlink? expectedLink);
                managed.Screen.TryGetHyperlink(right.HyperlinkId, out TerminalHyperlink? actualLink);
                Assert.Equal(expectedLink?.Uri, actualLink?.Uri);
                Assert.Equal(expectedLink is null ? [] : expectedLink.ExplicitId.ToArray(),
                    actualLink is null ? [] : actualLink.ExplicitId.ToArray());
            }
        }
        Assert.Equal(managed.Screen.TotalRows, rowIndex);
    }

    private static List<TerminalRow> Group(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> rows = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) rows.Add(row);
        return rows;
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
