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

// Ghostty Terminal.scrollUp/scrollDown move a temporary cursor; index instead
// uses Screen.cursorScrollRegionUp and PageList.eraseRow[Bounded]. WT scrolls
// rectangles without that PAGE cursor lifetime, and xterm.js splices BufferLines.
// Ghostty defines the allocator/rotation order tested here, not repeated DL.
public sealed class ManagedScrollOrchestrationTests
{
    [Theory]
    [InlineData("", 1, true)]
    [InlineData("\u001b[2;4r", 2, true)]
    [InlineData("\u001b[2;4r", 1, false)]
    [InlineData("\u001b[2;4r", 3, false)]
    [InlineData("\u001b[?69h\u001b[2;7s", 1, false)]
    public void ReverseIndexPreservesPendingWrapOnlyWhenItScrolls(string margins, int row, bool pending)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 4));
        Process(processor, margins + $"\u001b[{row};8HX\u001bM");
        Assert.Equal(pending, Cursor(processor).PendingWrap);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryUntrackedIndexRotatesOwnedRowsWithoutCreatingSnapshotPages(bool alternate)
    {
        TerminalScreen screen = new(8, 4, 100);
        using BasicVtProcessor processor = new(screen);
        if (alternate) processor.Process("\u001b[?47h"u8);
        else processor.Process("\u001b[2;4r"u8);
        int top = alternate ? 0 : 1;
        TerminalRow erased = screen.GetViewportRow(top), survivor = screen.GetViewportRow(top + 1);
        survivor[0].Codepoint = 'A';
        survivor.WrapsToNext = true;
        TerminalScreen retained = screen.CreateStateCopy();

        Process(processor, "\u001b[4;4H\n");

        Assert.False(screen.TracksSnapshotMetadata);
        Assert.Same(survivor, screen.GetViewportRow(top));
        Assert.Same(erased, screen.GetViewportRow(3));
        Assert.True(survivor.WrapsToNext);
        Assert.Same(retained.GetViewportRow(top + 1).SearchStorageIdentity, survivor.SearchStorageIdentity);
        Assert.Equal(4, screen.TotalRows);
        for (int row = 0; row < 4; row++) Assert.Null(screen.GetViewportRow(row).SnapshotAllocation);
    }

    [Theory]
    [InlineData('S', false)]
    [InlineData('T', false)]
    [InlineData('S', true)]
    [InlineData('T', true)]
    public void TemporaryCursorVisitsTheTopPageAndReissuesItsImplicitLink(char operation, bool rectangle)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(BlankRows(), 1));
        string margins = rectangle ? "\u001b[?69h\u001b[2;7s" : "";
        Process(terminal.Processor, margins + "\u001b[2;4r\u001b[4;4H\u001b[1m\u001b]8;;u\a");
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        Assert.Equal(0, terminal.Screen.GetViewportRow(1).SnapshotAllocation!.Capacity.Styles);

        Process(terminal.Processor, $"\u001b[2{operation}");

        Assert.Equal((3, 3), (terminal.Processor.CursorCol, terminal.Processor.CursorRow));
        Assert.Equal(16, terminal.Screen.GetViewportRow(1).SnapshotAllocation!.Capacity.Styles);
        Assert.Equal(0, retained.GetViewportRow(1).SnapshotAllocation!.Capacity.Styles);
        GhosttySnapshotScreenState cursor = Cursor(terminal.Processor);
        Assert.Equal(3U, cursor.HyperlinkImplicitCounter);
        Assert.True(cursor.TryGetHyperlink(out GhosttySnapshotHyperlink link));
        Assert.Equal(2U, link.ImplicitId);
        terminal.Processor.Process("X"u8);
        TerminalCell cell = terminal.Screen.GetViewportRow(3).ReadOnlyCells[3];
        Assert.Equal(CellAttributes.Bold, cell.Attributes);
        Assert.True(terminal.Screen.TryGetHyperlink(cell.HyperlinkId, out TerminalHyperlink? written));
        Assert.Equal(2U, written!.ImplicitId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullScreenSuVisitsTheBottomBeforeRestoringTheVisibleCursor(bool alternate)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(BlankRows(), 1, alternate));
        Process(terminal.Processor, "\u001b[1;4H\u001b[1m\u001b]8;;u\a");
        GhosttySnapshotPageAllocation bottom = terminal.Screen.GetViewportRow(3).SnapshotAllocation!;
        Process(terminal.Processor, "\u001b[S");

        Assert.Equal((3, 0), (terminal.Processor.CursorCol, terminal.Processor.CursorRow));
        Assert.Equal(alternate ? 4 : 5, terminal.Screen.TotalRows);
        // Both old bottom and final cursor pages were visited even though no
        // bold cells were printed; visible-state-only accounting misses this.
        int bottomRow = alternate ? 3 : 2;
        Assert.NotSame(bottom, terminal.Screen.GetViewportRow(bottomRow).SnapshotAllocation);
        Assert.Equal(16, terminal.Screen.GetViewportRow(bottomRow).SnapshotAllocation!.Capacity.Styles);
        Assert.Equal(16, terminal.Screen.GetViewportRow(0).SnapshotAllocation!.Capacity.Styles);
        GhosttySnapshotScreenState cursor = Cursor(terminal.Processor);
        Assert.Equal(alternate ? 3U : 4U, cursor.HyperlinkImplicitCounter);
        Assert.True(cursor.TryGetHyperlink(out GhosttySnapshotHyperlink link));
        Assert.Equal(alternate ? 2U : 3U, link.ImplicitId);
    }

    [Theory]
    [InlineData("\n", 4)]
    [InlineData("\u001bD", 4)]
    [InlineData("\u001b[S", 4)]
    [InlineData("\n", 2)]
    [InlineData("\u001bD", 2)]
    [InlineData("\u001b[S", 2)]
    [InlineData("\n", 1)]
    [InlineData("\u001b[S", 1)]
    public void AlternateRotationRetainsWrapSpacersMetadataAndPhysicalSlots(string command, int pageRows)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(ContentRows(), pageRows, alternate: true));
        Process(terminal.Processor, "\u001b[4;4H");
        TerminalRow[] before = ViewportRows(terminal.Screen);
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        TerminalScreenAnchor anchor = terminal.Screen.CreateAnchor(1, 3);

        Process(terminal.Processor, command);

        Assert.Equal(4, terminal.Screen.TotalRows);
        Assert.Equal((3, 3), (terminal.Processor.CursorCol, terminal.Processor.CursorRow));
        Assert.True(terminal.Screen.TryResolveAnchor(anchor, out TerminalGridPosition moved));
        Assert.Equal(new TerminalGridPosition(3, 0), moved);
        for (int row = 0; row < 3; row++)
        {
            TerminalRow actual = terminal.Screen.GetViewportRow(row);
            TerminalRow expected = retained.GetViewportRow(row + 1);
            AssertRow(expected, actual);
            int pageEnd = Math.Min(3, (row / pageRows + 1) * pageRows - 1);
            bool cloned = row == pageEnd;
            Assert.Same(before[cloned ? row / pageRows * pageRows : row + 1], actual);
            if (!cloned) Assert.Same(expected.SearchStorageIdentity, actual.SearchStorageIdentity);
            Assert.Equal(cloned ? 0 : (row + 1) % pageRows, actual.SnapshotAllocationRow);
        }
        TerminalRow blank = terminal.Screen.GetViewportRow(3);
        Assert.Same(before[4 - pageRows], blank);
        Assert.Equal(0, blank.SnapshotAllocationRow);
        Assert.False(blank.WrapsToNext);
        Assert.False(blank.IsWrapContinuation);
        Assert.Equal(TerminalSemanticPrompt.None, blank.SemanticPrompt);
        foreach (TerminalCell cell in blank.ReadOnlyCells)
        {
            Assert.Equal(0, cell.Codepoint);
            Assert.Null(cell.Grapheme);
            Assert.Equal(CellAttributes.None, cell.Attributes);
        }
        for (int row = 0; row < 4; row++) Assert.Equal('A' + row, retained.GetViewportRow(row).ReadOnlyCells[3].Codepoint);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void BoundedIndexRotatesOnlyItsRegionAndKeepsTheCursorPage(bool alternate, int pageRows)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(ContentRows(), pageRows, alternate));
        Process(terminal.Processor, "\u001b[2;3r\u001b[3;4H\u001b[44m\u001b]8;;u\a");
        uint counter = Cursor(terminal.Processor).HyperlinkImplicitCounter;
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        GhosttySnapshotPageAllocation cursorPage = terminal.Screen.GetViewportRow(2).SnapshotAllocation!;

        terminal.Processor.Process("\n"u8);

        Assert.Equal((3, 2), (terminal.Processor.CursorCol, terminal.Processor.CursorRow));
        Assert.Same(cursorPage, terminal.Screen.GetViewportRow(2).SnapshotAllocation);
        Assert.Equal(counter, Cursor(terminal.Processor).HyperlinkImplicitCounter);
        AssertRow(retained.GetViewportRow(0), terminal.Screen.GetViewportRow(0));
        AssertRow(retained.GetViewportRow(2), terminal.Screen.GetViewportRow(1));
        AssertRow(retained.GetViewportRow(3), terminal.Screen.GetViewportRow(3));
        TerminalRow blank = terminal.Screen.GetViewportRow(2);
        Assert.False(blank.WrapsToNext);
        Assert.False(blank.IsWrapContinuation);
        Assert.Equal(TerminalSemanticPrompt.None, blank.SemanticPrompt);
        foreach (TerminalCell cell in blank.ReadOnlyCells)
        {
            Assert.Equal(0, cell.Codepoint);
            Assert.Equal(TerminalColorIdentity.Palette(4), cell.BackgroundIdentity);
            Assert.Equal(0, cell.HyperlinkId);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void HeldRotationPublishesRowsAndAllocatorsTogether(int pageRows)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(ContentRows(), pageRows, alternate: true));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        Process(terminal.Processor, "\u001b[?2026h\u001b[4;4H\n");
        for (int row = 0; row < 4; row++) AssertRow(retained.GetViewportRow(row), terminal.Screen.GetViewportRow(row));
        Process(terminal.Processor, "\u001b[?2026l");
        for (int row = 0; row < 3; row++) AssertRow(retained.GetViewportRow(row + 1), terminal.Screen.GetViewportRow(row));
        Assert.Equal(0, terminal.Screen.GetViewportRow(3).ReadOnlyCells[3].Codepoint);
        for (int row = 0; row < 4; row++) Assert.Equal('A' + row, retained.GetViewportRow(row).ReadOnlyCells[3].Codepoint);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\u001b[S")]
    public void SingleRowAlternateClearsInPlaceWithoutHistory(string command)
    {
        TerminalRow[] rows = [new(8)];
        rows[0][0].Codepoint = 'A';
        rows[0].SemanticPrompt = TerminalSemanticPrompt.Prompt;
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(rows, 1, alternate: true));
        TerminalRow original = terminal.Screen.GetViewportRow(0);
        Process(terminal.Processor, "\u001b[44m" + command);
        Assert.Equal(1, terminal.Screen.TotalRows);
        Assert.Same(original, terminal.Screen.GetViewportRow(0));
        Assert.Equal(TerminalSemanticPrompt.None, original.SemanticPrompt);
        Assert.Equal(0, original.ReadOnlyCells[0].Codepoint);
        Assert.Equal(TerminalColorIdentity.Palette(4), original.ReadOnlyCells[0].BackgroundIdentity);
    }

    [Theory]
    [InlineData(false, 1, "\u001b[2;4r\u001b[4;4H", "\u001b[2S")]
    [InlineData(false, 1, "\u001b[2;4r\u001b[4;4H", "\u001b[2T")]
    [InlineData(false, 1, "\u001b[?69h\u001b[2;7s\u001b[2;4r\u001b[4;4H", "\u001b[S")]
    [InlineData(false, 1, "\u001b[1;4H", "\u001b[S")]
    [InlineData(true, 1, "\u001b[1;4H", "\u001b[S")]
    [InlineData(true, 2, "\u001b[4;4H", "\n")]
    [InlineData(true, 4, "\u001b[4;4H", "\u001b[2S")]
    [InlineData(false, 2, "\u001b[2;3r\u001b[3;4H", "\n")]
    [InlineData(true, 2, "\u001b[2;3r\u001b[3;4H", "\u001bD")]
    [InlineData(false, 1, "\u001b[2;4r\u001b[2;8HX", "\u001bM")]
    public void CursorAndRotationOrchestrationMatchNative(bool alternate, int pageRows, string setup, string command)
    {
        RequireNative();
        byte[] snapshot = Snapshot(ContentRows(), pageRows, alternate);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        foreach (string input in new[] { setup + "\u001b[44m\u001b]8;;u\a", command, "X\u001b]8;;\a" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes);
            managed.Processor.Process(bytes);
            AssertNative(native, managed);
        }
    }

    private static void AssertNative(GhosttyTerminal native, ManagedTerminalSnapshot managed)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        int key = managed.Screen.AlternateBufferActive ? 1 : 0;
        GhosttySnapshotScreen expected = ready.Screens[key];
        GhosttySnapshotScreenState cursor = Cursor(managed.Processor);
        Assert.Equal(expected.State.CursorX, cursor.CursorX);
        Assert.Equal(expected.State.CursorY, cursor.CursorY);
        Assert.Equal(expected.State.PendingWrap, cursor.PendingWrap);
        Assert.Equal(expected.State.Pen, cursor.Pen);
        Assert.Equal(expected.State.HyperlinkImplicitCounter, cursor.HyperlinkImplicitCounter);
        Assert.Equal(expected.State.TryGetHyperlink(out GhosttySnapshotHyperlink link), cursor.TryGetHyperlink(out GhosttySnapshotHyperlink actualLink));
        if (expected.State.TryGetHyperlink(out _)) Assert.Equal(link.ImplicitId, actualLink.ImplicitId);
        TerminalRowBuffer rows = managed.Screen.GetSnapshotRows(key)!;
        TerminalScreen owner = new(8, 1);
        int index = 0;
        foreach (GhosttySnapshotPage page in expected.Pages)
        foreach (TerminalRow row in GhosttySnapshotLivePage.Decode(page, owner))
        {
            AssertRow(row, rows[index]);
            Assert.Equal(page.Capacity, rows[index].SnapshotAllocation!.Capacity);
            for (int column = 0; column < 8; column++)
            {
                int expectedToken = row.ReadOnlyCells[column].HyperlinkId;
                int actualToken = rows[index].ReadOnlyCells[column].HyperlinkId;
                Assert.Equal(expectedToken == 0, actualToken == 0);
                if (expectedToken == 0) continue;
                Assert.True(owner.TryGetHyperlink(expectedToken, out TerminalHyperlink? expectedLink));
                Assert.True(managed.Screen.TryGetHyperlink(actualToken, out TerminalHyperlink? observedLink));
                Assert.Equal(expectedLink!.ImplicitId, observedLink!.ImplicitId);
                Assert.True(expectedLink.UriBytes.SequenceEqual(observedLink.UriBytes));
            }
            index++;
        }
        Assert.Equal(rows.Count, index);
    }

    private static void AssertRow(TerminalRow expected, TerminalRow actual)
    {
        Assert.Equal(expected.WrapsToNext, actual.WrapsToNext);
        Assert.Equal(expected.IsWrapContinuation, actual.IsWrapContinuation);
        Assert.Equal(expected.SemanticPrompt, actual.SemanticPrompt);
        for (int column = 0; column < expected.Columns; column++)
        {
            TerminalCell cell = expected.ReadOnlyCells[column], other = actual.ReadOnlyCells[column];
            Assert.Equal(cell.Codepoint, other.Codepoint);
            Assert.Equal(cell.Grapheme, other.Grapheme);
            Assert.Equal(cell.Width, other.Width);
            Assert.Equal(cell.IsWideSpacerHead, other.IsWideSpacerHead);
            Assert.Equal(cell.IsProtected, other.IsProtected);
            Assert.Equal(cell.SemanticContent, other.SemanticContent);
            Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in cell), GhosttySnapshotLivePage.EncodeStyle(in other));
        }
    }

    private static TerminalRow[] ViewportRows(TerminalScreen screen)
    {
        TerminalRow[] rows = new TerminalRow[screen.ViewportRows];
        for (int row = 0; row < rows.Length; row++) rows[row] = screen.GetViewportRow(row);
        return rows;
    }

    private static TerminalRow[] BlankRows() => [new(8), new(8), new(8), new(8)];

    private static TerminalRow[] ContentRows()
    {
        TerminalRow[] rows = BlankRows();
        CellAttributes[] attributes = [CellAttributes.Bold, CellAttributes.Italic, CellAttributes.Dim, CellAttributes.Underline];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row][3].Codepoint = 'A' + row;
            rows[row][3].Attributes = attributes[row];
            rows[row].WrapsToNext = row < 2;
            rows[row].IsWrapContinuation = row is 1 or 2;
        }
        rows[0].SemanticPrompt = TerminalSemanticPrompt.Prompt;
        rows[1].SemanticPrompt = TerminalSemanticPrompt.PromptContinuation;
        rows[1][7].Width = 0;
        rows[1][7].IsWideSpacerHead = true;
        rows[2][0].Codepoint = '界';
        rows[2][0].Width = 2;
        rows[2][1].Width = 0;
        return rows;
    }

    private static byte[] Snapshot(TerminalRow[] rows, int pageRows, bool alternate = false)
    {
        TerminalScreen owner = new(8, rows.Length, 1000);
        using BasicVtProcessor template = new(owner);
        if (alternate) template.Process("\u001b[?47h"u8);
        using GhosttySnapshotRecordReader reader = new(template.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        bool active = false;
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen)
            {
                active = BinaryPrimitives.ReadUInt16LittleEndian(bytes) == (alternate ? 1 : 0);
                if (active) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), checked((ushort)((rows.Length + pageRows - 1) / pageRows)));
            }
            if (tag == GhosttySnapshotRecordTag.Page && active)
            {
                for (int offset = 0; offset < rows.Length; offset += pageRows)
                {
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture(rows.AsSpan(offset, Math.Min(pageRows, rows.Length - offset)), owner, 8 * pageRows).WritePayloadTo(output);
                    records.Add(new(tag, output.ToArray()));
                }
            }
            else records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static GhosttySnapshotScreenState Cursor(BasicVtProcessor processor)
    {
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        return ready.Screens[ready.Terminal.Header.ActiveScreenKey].State;
    }

    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
