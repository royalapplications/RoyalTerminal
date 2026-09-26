// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Terminal.eraseLine expands a split wide glyph before clearing, and
// eraseDisplay(.above) clears the cursor row before preceding rows. Match its
// colors, metadata lifetime and wrap semantics. xterm.js BufferLine.replaceCells
// likewise fills split halves with erase attributes; WT EraseInDisplay/Line
// uses erase-attribute rectangles (including image content), but orders ED1's
// rows differently and has no Ghostty PAGE allocator. Keep Ghostty's ordering.
public sealed partial class ManagedEraseParityTests
{
    private const string Close = "\u001b]8;;\u001b\\";
    private const string Wide = "\u001b[1;41m\u001b]8;id=edge;https://edge\u001b\\中\u0301" + Close;

    [Theory]
    [InlineData('K', 0)]
    [InlineData('K', 1)]
    [InlineData('K', 2)]
    [InlineData('J', 0)]
    [InlineData('J', 1)]
    [InlineData('J', 2)]
    public void EraseRangesAndSubsequentWritesMatchNative(char operation, int mode)
    {
        RequireNative();
        // 2,592 erasures: every viewport cell, three wide/wrapped layouts,
        // ordinary/selective commands, and absent/DEC/ISO protection.
        foreach (string prefix in new[] { "", "A", "abcdefg" })
        for (int protection = 0; protection < 6; protection++)
        {
            string on = (protection / 2) switch { 1 => "\u001b[1\"q", 2 => "\u001bV", _ => "" };
            string off = (protection / 2) switch { 1 => "\u001b[0\"q", 2 => "\u001bW", _ => "" };
            using GhosttyTerminal source = new(8, 3, 1024 * 1024);
            source.Write(Encoding.UTF8.GetBytes("\u001b]133;A\aabcdefgh\r\n" + prefix + on + Wide + off +
                "\u001b[3;42m文\u0302\u001b[0mABCD\u001b]133;C\a\r\nlast\u001b[44m"));
            byte[] snapshot = GhosttySnapshot.Encode(source);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 8; column++)
            {
                using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
                using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
                TerminalScreen retained = managed.Screen.CreateStateCopy();
                TerminalCell[][] retainedCells = new TerminalCell[retained.TotalRows][];
                for (int index = 0; index < retainedCells.Length; index++)
                    retainedCells[index] = retained.GetRow(index).ReadOnlyCells.ToArray();
                string context = $"{mode}{operation}, prefix={prefix.Length}, protection={protection}, row={row}, col={column}";
                byte[] command = Encoding.UTF8.GetBytes($"\u001b[{row + 1};{column + 1}H\u001b[{(protection % 2 == 1 ? "?" : "")}{mode}{operation}");
                native.Write(command);
                managed.Processor.Process(command);
                AssertNative(native, managed, context);
                for (int index = 0; index < retainedCells.Length; index++)
                    Assert.Equal(retainedCells[index], retained.GetRow(index).ReadOnlyCells.ToArray());
                byte[] follow = Encoding.UTF8.GetBytes("\u001b[4;45m\u001b]8;id=next;https://next\u001b\\Z\u0303" + Close);
                native.Write(follow);
                managed.Processor.Process(follow);
                AssertNative(native, managed, context + ", subsequent write");
            }
        }
    }

    [Theory]
    [InlineData('K', 0, false, false)]
    [InlineData('K', 0, false, true)]
    [InlineData('K', 0, true, false)]
    [InlineData('K', 0, true, true)]
    [InlineData('K', 1, false, false)]
    [InlineData('K', 1, false, true)]
    [InlineData('K', 1, true, false)]
    [InlineData('K', 1, true, true)]
    [InlineData('J', 0, false, false)]
    [InlineData('J', 0, false, true)]
    [InlineData('J', 0, true, false)]
    [InlineData('J', 0, true, true)]
    [InlineData('J', 1, false, false)]
    [InlineData('J', 1, false, true)]
    [InlineData('J', 1, true, false)]
    [InlineData('J', 1, true, true)]
    public void ErasingEitherHalfReleasesMetadataWithCurrentBackground(char operation, int mode, bool restored, bool held)
    {
        TerminalScreen initial = new(8, 3) { SnapshotScrollbackQuota = new() };
        using BasicVtProcessor live = new(initial);
        live.Process(Encoding.UTF8.GetBytes("\u001b[2;3H" + Wide));
        using ManagedTerminalSnapshot? copy = restored ? ManagedTerminalSnapshot.Restore(live.GetBinarySnapshot()) : null;
        BasicVtProcessor processor = copy?.Processor ?? live;
        TerminalScreen screen = copy?.Screen ?? initial;
        TerminalScreen retained = screen.CreateStateCopy();
        if (held) processor.Process("\u001b[?2026h"u8);
        processor.Process(Encoding.UTF8.GetBytes($"\u001b[0;44m\u001b[2;{(mode == 0 ? 4 : 3)}H\u001b[{mode}{operation}"));
        if (held)
        {
            Assert.Equal("中\u0301", screen.GetViewportRow(1).ReadOnlyCells[2].Grapheme);
            processor.Process("\u001b[?2026l"u8);
        }
        TerminalRow row = screen.GetViewportRow(1);
        for (int column = 2; column < 4; column++)
        {
            TerminalCell cell = row.ReadOnlyCells[column];
            Assert.Equal(0, cell.Codepoint);
            Assert.Equal((byte)1, cell.Width);
            Assert.Equal(TerminalColorIdentity.Palette(4), cell.BackgroundIdentity);
            Assert.Null(cell.Grapheme);
            Assert.Equal(0, cell.HyperlinkId);
            Assert.Equal(CellAttributes.None, cell.Attributes);
        }
        List<TerminalRow> group = [];
        foreach (TerminalRow member in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(member.SnapshotAllocation, row.SnapshotAllocation)) group.Add(member);
        Assert.True(screen.TryGetSnapshotGraphemeUsage(row.SnapshotAllocation!, group, out ulong graphemes, out ulong bytes));
        Assert.Equal((0UL, 0UL), (graphemes, bytes));
        Assert.True(screen.TryGetSnapshotHyperlinkUsage(row.SnapshotAllocation!, group, out ulong links, out ulong cells, out _));
        Assert.Equal((0UL, 0UL), (links, cells));
        Assert.True(screen.TryGetSnapshotStyleUsage(row.SnapshotAllocation!, group, out int styles));
        Assert.Equal(1, styles);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal("中\u0301", retained.GetViewportRow(1).ReadOnlyCells[2].Grapheme);
        Assert.Equal(TerminalColorIdentity.Palette(1), retained.GetViewportRow(1).ReadOnlyCells[3].BackgroundIdentity);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompleteDisplayEraseAtProtectedPromptPreservesHistory(bool iso, bool selective)
    {
        RequireNative();
        using GhosttyTerminal source = new(8, 3, 1024 * 1024);
        source.Write(Encoding.UTF8.GetBytes("output\r\nmore\r\n\u001b]133;A\a" +
            (iso ? "\u001bV" : "\u001b[1\"q") + Wide + (iso ? "\u001bW" : "\u001b[0\"q") + "\u001b[0;44m"));
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        AssertNative(native, managed, "restored prompt");
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        byte[] command = Encoding.UTF8.GetBytes($"\u001b[{(selective ? "?" : "")}2J");
        native.Write(command);
        managed.Processor.Process(command);
        AssertNative(native, managed, $"prompt ED2, iso={iso}, selective={selective}");
        Assert.Equal(6, managed.Screen.TotalRows);
        Assert.Equal("中\u0301", managed.Screen.GetRow(2).ReadOnlyCells[0].Grapheme);
        Assert.True(managed.Screen.GetRow(2).ReadOnlyCells[0].IsProtected);
        Assert.Equal("中\u0301", retained.GetViewportRow(2).ReadOnlyCells[0].Grapheme);
        native.Write(command);
        managed.Processor.Process(command);
        AssertNative(native, managed, "repeated prompt ED2");
        Assert.Equal(6, managed.Screen.TotalRows);
        native.Write("Z"u8);
        managed.Processor.Process("Z"u8);
        AssertNative(native, managed, "write after prompt ED2");
    }

    [Theory]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    [InlineData(22, false, false)]
    [InlineData(22, false, true)]
    [InlineData(22, true, false)]
    [InlineData(22, true, true)]
    public void ScrollClearMigratesCursorStyleAndLinkAcrossPages(int mode, bool explicitId, bool held)
    {
        RequireNative();
        // Mirrors Ghostty's scrollClear cursorReload regression, using a
        // compact restored page so clearing crosses its allocation boundary.
        using GhosttyTerminal source = new(8, 3, 1024 * 1024);
        source.Write(Encoding.UTF8.GetBytes("first\r\nsecond\r\n\u001b]133;A\a" + Wide +
            "\u001b[1;3H\u001b[3;44m\u001b]8;" + (explicitId ? "id=cursor" : "") + ";https://cursor\a"));
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        if (held) managed.Processor.Process("\u001b[?2026h"u8);
        byte[] command = Encoding.UTF8.GetBytes($"\u001b[{mode}J");
        native.Write(command);
        managed.Processor.Process(command);
        if (held)
        {
            Assert.Equal(3, managed.Screen.TotalRows);
            managed.Processor.Process("\u001b[?2026l"u8);
        }
        AssertNative(native, managed, $"cross-page ED{mode}, explicit={explicitId}, held={held}");
        Assert.Equal(3, retained.TotalRows);
        Assert.Equal("中\u0301", retained.GetViewportRow(2).ReadOnlyCells[0].Grapheme);
        byte[] follow = Encoding.UTF8.GetBytes("B\u0303" + Close + "\r\nC");
        native.Write(follow);
        managed.Processor.Process(follow);
        AssertNative(native, managed, "write after cursor migration");
    }

    [Theory]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("X\r\n\u001b[44m\u001b[2K\u001b[0m")]
    [InlineData("\u001b]133;A\a")]
    public void ScrollClearRetainsCursorPinsAndNonTextRowContent(string contents)
    {
        RequireNative();
        using GhosttyTerminal source = new(8, 3, 1024 * 1024);
        source.Write(Encoding.UTF8.GetBytes(contents + "\u001b[3;5H"));
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        native.Write("\u001b[22J"u8);
        managed.Processor.Process("\u001b[22J"u8);
        AssertNative(native, managed, "partial scroll clear " + Convert.ToHexString(Encoding.UTF8.GetBytes(contents)));
        native.Write("Z"u8);
        managed.Processor.Process("Z"u8);
        AssertNative(native, managed, "write after partial scroll clear");
    }

    [Fact]
    public void EraseAboveClearsCursorRowRasterButRetainsFollowingRows()
    {
        // Sixel is a managed host extension; use WT's erase-rectangle policy.
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor processor = new(screen) { SixelGraphicsEnabled = true };
        processor.NotifyResize(8, 3, 80, 30);
        ReadOnlySpan<byte> pixel = "\u001bPq\"1;1;1;1#1;2;100;0;0#1@\u001b\\"u8;
        processor.Process(pixel);
        processor.Process("\u001b[3;1H"u8);
        processor.Process(pixel);
        Assert.Equal(2, screen.GetRasterImagePlacements().Length);
        processor.Process("\u001b[1;1H\u001b[1J"u8);
        Assert.Equal(1, screen.GetRasterImagePlacements().Length);
        processor.Process("\u001b[3;1H\u001b[1J"u8);
        Assert.False(screen.HasRasterGraphics);
    }

    private static void AssertNative(GhosttyTerminal native, ManagedTerminalSnapshot managed, string context)
    {
        int key = managed.Screen.AlternateBufferActive ? 1 : 0;
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotScreen expected = reader.ReadReady().Screens[key];
        using GhosttySnapshotStateReader actualReader = new(managed.Processor.GetBinarySnapshot(), new());
        GhosttySnapshotScreen actual = actualReader.ReadReady().Screens[key];
        Assert.True(expected.State.HistoryRows == actual.State.HistoryRows,
            $"{context}: expected history {expected.State.HistoryRows}, actual {actual.State.HistoryRows}");
        Assert.Equal((expected.State.CursorX, expected.State.CursorY, expected.State.PendingWrap),
            (actual.State.CursorX, actual.State.CursorY, actual.State.PendingWrap));
        Assert.Equal(expected.State.Pen, actual.State.Pen);
        Assert.True(expected.State.HyperlinkImplicitCounter == actual.State.HyperlinkImplicitCounter,
            $"{context}: expected hyperlink counter {expected.State.HyperlinkImplicitCounter}, actual {actual.State.HyperlinkImplicitCounter}");
        Assert.Equal(expected.State.TryGetHyperlink(out GhosttySnapshotHyperlink expectedCursorLink),
            actual.State.TryGetHyperlink(out GhosttySnapshotHyperlink actualCursorLink));
        if (expected.State.TryGetHyperlink(out _))
        {
            Assert.Equal(expectedCursorLink.ImplicitId, actualCursorLink.ImplicitId);
            Assert.True(expectedCursorLink.Uri.SequenceEqual(actualCursorLink.Uri));
            Assert.True(expectedCursorLink.ExplicitId.SequenceEqual(actualCursorLink.ExplicitId));
        }
        List<GhosttySnapshotPage> pages = [.. expected.Pages];
        while (reader.ReadNextHistoryPage() is { } history)
            if (history.Key == key) pages.Insert(0, history.Page);
        TerminalScreen owner = new(8, 3);
        int index = 0;
        foreach (GhosttySnapshotPage page in pages)
        foreach (TerminalRow row in GhosttySnapshotLivePage.Decode(page, owner))
        {
            TerminalRow observed = managed.Screen.GetRow(index++);
            // PAGE encodes used rows, not a grown page's unused row slots.
            GhosttySnapshotPageCapacity capacity = observed.SnapshotAllocation!.Capacity;
            Assert.True(capacity.Rows >= page.Capacity.Rows);
            Assert.Equal(page.Capacity with { Rows = capacity.Rows }, capacity);
            Assert.False(observed.SnapshotAllocation.MetadataOverflow);
            Assert.True((row.WrapsToNext, row.IsWrapContinuation, row.SemanticPrompt) ==
                (observed.WrapsToNext, observed.IsWrapContinuation, observed.SemanticPrompt), $"{context}, row={index - 1}: row metadata differs");
            for (int column = 0; column < row.Columns; column++)
            {
                TerminalCell left = row.ReadOnlyCells[column], right = observed.ReadOnlyCells[column];
                string position = $"{context}, row={index - 1}, col={column}";
                Assert.True((left.Codepoint, left.Width, left.Grapheme, left.IsWideSpacerHead, left.IsProtected, left.SemanticContent) ==
                    (right.Codepoint, right.Width, right.Grapheme, right.IsWideSpacerHead, right.IsProtected, right.SemanticContent),
                    $"{position}: expected {left.Codepoint}/{left.Width}/{left.SemanticContent}, actual {right.Codepoint}/{right.Width}/{right.SemanticContent}");
                Assert.True(GhosttySnapshotLivePage.EncodeStyle(in left) == GhosttySnapshotLivePage.EncodeStyle(in right), $"{position}: style differs");
                owner.TryGetHyperlink(left.HyperlinkId, out TerminalHyperlink? expectedLink);
                managed.Screen.TryGetHyperlink(right.HyperlinkId, out TerminalHyperlink? actualLink);
                Assert.Equal(expectedLink?.Uri, actualLink?.Uri);
                Assert.Equal(expectedLink is null ? [] : expectedLink.ExplicitId.ToArray(),
                    actualLink is null ? [] : actualLink.ExplicitId.ToArray());
            }
        }
        Assert.Equal(managed.Screen.TotalRows, index);
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
