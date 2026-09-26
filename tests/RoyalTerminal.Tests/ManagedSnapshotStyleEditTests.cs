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

// Ghostty Screen.clearCells, Page.clonePartialRowFrom and Terminal ICH/DCH
// define reference release/copy/swap order. WT ROW and xterm.js BufferLine
// provide visible-cell references, not this native PAGE allocation contract.
public sealed class ManagedSnapshotStyleEditTests
{
    [Fact]
    public void InPageStorageSwapMovesCowOwnershipWithoutCopyingArrays()
    {
        TerminalRow first = StyledRow(CellAttributes.Bold, CellAttributes.Bold, CellAttributes.Bold, CellAttributes.Bold);
        TerminalRow second = StyledRow(CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic);
        first.SemanticPrompt = TerminalSemanticPrompt.Prompt;
        TerminalRow retained = first.CreateStateCopy();
        object firstStorage = first.SearchStorageIdentity, secondStorage = second.SearchStorageIdentity;
        first.SwapActiveStorage(second);
        Assert.Same(secondStorage, first.SearchStorageIdentity);
        Assert.Same(firstStorage, second.SearchStorageIdentity);
        Assert.Equal(TerminalSemanticPrompt.Prompt, second.SemanticPrompt);
        second[0].Attributes = CellAttributes.None;
        Assert.Equal(CellAttributes.Bold, retained.ReadOnlyCells[0].Attributes);
        Assert.NotSame(firstStorage, second.SearchStorageIdentity);
        Assert.Equal(CellAttributes.Italic, first.ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void InPageShiftsPreservePublishedRowsAndCapacity()
    {
        TerminalRow first = StyledRow(CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic);
        TerminalRow second = StyledRow(CellAttributes.Bold, CellAttributes.Dim, CellAttributes.Bold, CellAttributes.Dim);
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([first, second], onePage: true));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        ushort capacity = Capacity(terminal.Screen, 0);
        terminal.Processor.Process("\u001b[L"u8);
        Assert.Equal(capacity, Capacity(terminal.Screen, 1));
        Assert.Equal(CellAttributes.Italic, terminal.Screen.GetViewportRow(1).ReadOnlyCells[0].Attributes);
        Assert.Equal(CellAttributes.Bold, retained.GetViewportRow(1).ReadOnlyCells[0].Attributes);
    }

    [Fact]
    public void WholeDestinationIsReleasedBeforeCopyingNewStylesFromAnotherPage()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(CopySnapshot());
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.Process("\u001b[L"u8);
        Assert.Equal(4, Capacity(terminal.Screen, 1));
        Assert.Equal(CellAttributes.Italic, terminal.Screen.GetViewportRow(1).ReadOnlyCells[0].Attributes);
        Assert.Equal(CellAttributes.Bold, retained.GetViewportRow(1).ReadOnlyCells[0].Attributes);
        Assert.Equal(4, Capacity(retained, 1));
        terminal.Processor.Process("\u001b[2;1H\u001b[2K\u001b[2m"u8);
        Assert.Equal(4, Capacity(terminal.Screen, 1));
    }

    [Fact]
    public void CopyPressureIsRetainedEvenWhenTheCopiedCellsAreImmediatelyErased()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(CopySnapshot(threeStyles: true));
        terminal.Processor.Process("\u001b[L\u001b[2;1H\u001b[2K"u8);
        Assert.Equal(8, Capacity(terminal.Screen, 1));
        foreach (ref readonly TerminalCell cell in terminal.Screen.GetViewportRow(1).ReadOnlyCells)
            Assert.Equal(CellAttributes.None, cell.Attributes);
    }

    [Theory]
    [InlineData("\u001b[X")]
    [InlineData("\u001b[1K")]
    [InlineData("\u001b[?1K")]
    public void ErasingAnIdenticalBackgroundOnlyCellStillReleasesItsStyleReference(string erase)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(BackgroundSnapshot());
        terminal.Processor.Process(Encoding.UTF8.GetBytes("\u001b[41m" + erase + "\u001b[0;2m"));
        Assert.Equal(4, Capacity(terminal.Screen, 0));
        Assert.Equal(CellAttributes.Italic, terminal.Screen.GetViewportRow(0).ReadOnlyCells[1].Attributes);
    }

    [Fact]
    public void SelectiveEraseReleasesOnlyUnprotectedRuns()
    {
        TerminalRow row = StyledRow(CellAttributes.Bold, CellAttributes.Italic, CellAttributes.Bold, CellAttributes.Italic);
        row[0].IsProtected = true;
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([row]));
        terminal.Processor.Process("\u001b[?2K\u001b[2m"u8);
        Assert.Equal(4, Capacity(terminal.Screen, 0));
        Assert.Equal(CellAttributes.Bold, terminal.Screen.GetViewportRow(0).ReadOnlyCells[0].Attributes);
        Assert.True(terminal.Screen.GetViewportRow(0).ReadOnlyCells[0].IsProtected);
        for (int i = 1; i < 4; i++) Assert.Equal(CellAttributes.None, terminal.Screen.GetViewportRow(0).ReadOnlyCells[i].Attributes);
    }

    [Fact]
    public void HeldRowCopiesRetainThePublishedAllocatorUntilRelease()
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(CopySnapshot(threeStyles: true));
        terminal.Processor.Process("\u001b[?2026h\u001b[L"u8);
        Assert.Equal(4, Capacity(terminal.Screen, 1));
        terminal.Processor.Process("\u001b[?2026l"u8);
        Assert.Equal(8, Capacity(terminal.Screen, 1));
    }

    [Theory]
    [InlineData("\u001b[1;2H\u001b[@", "A\0BC")]
    [InlineData("\u001b[1;2H\u001b[P", "ACD\0")]
    public void CharacterShiftsKeepStylesWithCellsAndReleaseTheVacatedRun(string input, string expected)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot([
            StyledRow(CellAttributes.Bold, CellAttributes.Italic, CellAttributes.Bold, CellAttributes.Italic)]));
        terminal.Processor.Process(Encoding.UTF8.GetBytes(input));
        ReadOnlySpan<TerminalCell> cells = terminal.Screen.GetViewportRow(0).ReadOnlyCells;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal((int)expected[i], cells[i].Codepoint);
            Assert.Equal(expected[i] switch { 'A' or 'C' => CellAttributes.Bold, 'B' or 'D' => CellAttributes.Italic, _ => CellAttributes.None }, cells[i].Attributes);
        }
        Assert.Equal(4, Capacity(terminal.Screen, 0));
    }

    [Fact]
    public void CellEditsAndCopyGrowthMatchNativePageCapacities()
    {
        RequireNative();
        (byte[] Snapshot, string[] Inputs)[] cases =
        [
            (CopySnapshot(), ["\u001b[L", "\u001b[2;1H\u001b[2K\u001b[2m"]),
            (CopySnapshot(true), ["\u001b[L", "\u001b[2;1H\u001b[2K"]),
            (BackgroundSnapshot(), ["\u001b[41m\u001b[X", "\u001b[0;2m"]),
            (Snapshot([StyledRow(CellAttributes.Bold, CellAttributes.Italic, CellAttributes.Bold, CellAttributes.Italic)]),
                ["\u001b[1;2H\u001b[@", "\u001b[P", "\u001b[2K\u001b[2mX\u001b[0m"]),
            (CopySnapshot(), ["\u001b[?69h\u001b[2;4s\u001b[L", "\u001b[M"]),
            (Snapshot([StyledRow(CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic),
                StyledRow(CellAttributes.Bold, CellAttributes.Dim, CellAttributes.Bold, CellAttributes.Dim)], onePage: true),
                ["\u001b[?69h\u001b[2;4s\u001b[L", "\u001b[M", "\u001b[?69l\u001b[L"]),
        ];
        foreach ((byte[] snapshot, string[] inputs) in cases)
        {
            using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
            using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
            foreach (string input in inputs)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                native.Write(bytes); managed.Processor.Process(bytes);
                using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
                GhosttySnapshotScreen actual = reader.ReadReady().Screens[0];
                int row = 0;
                foreach (GhosttySnapshotPage page in actual.Pages)
                {
                    TerminalRow[] decoded = GhosttySnapshotLivePage.Decode(page, new TerminalScreen(4, 1));
                    foreach (TerminalRow expectedRow in decoded)
                    {
                        Assert.Equal(page.Capacity.Styles, Capacity(managed.Screen, row));
                        for (int column = 0; column < 4; column++)
                        {
                            TerminalCell expected = expectedRow.ReadOnlyCells[column];
                            TerminalCell observed = managed.Screen.GetViewportRow(row).ReadOnlyCells[column];
                            Assert.Equal(expected.Codepoint, observed.Codepoint);
                            Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in expected), GhosttySnapshotLivePage.EncodeStyle(in observed));
                        }
                        row++;
                    }
                }
                Assert.Equal(managed.Screen.ViewportRows, row);
            }
        }
    }

    private static ushort Capacity(TerminalScreen screen, int row) => screen.GetViewportRow(row).SnapshotAllocation!.Capacity.Styles;

    private static byte[] CopySnapshot(bool threeStyles = false) => Snapshot([
        StyledRow(CellAttributes.Italic, threeStyles ? CellAttributes.Underline : CellAttributes.Italic,
            threeStyles ? CellAttributes.Strikethrough : CellAttributes.Italic, CellAttributes.Italic),
        StyledRow(CellAttributes.Bold, CellAttributes.Dim, CellAttributes.Bold, CellAttributes.Dim)]);

    private static byte[] BackgroundSnapshot()
    {
        TerminalRow row = StyledRow(CellAttributes.None, CellAttributes.Italic, CellAttributes.None, CellAttributes.None);
        row[0].Codepoint = 0;
        row[0].BackgroundIdentity = TerminalColorIdentity.Palette(1);
        return Snapshot([row]);
    }

    private static TerminalRow StyledRow(params CellAttributes[] styles)
    {
        TerminalRow row = new(styles.Length);
        for (int i = 0; i < styles.Length; i++)
        {
            row[i].Codepoint = 'A' + i;
            row[i].Attributes = styles[i];
        }
        return row;
    }

    private static byte[] Snapshot(TerminalRow[] rows, bool onePage = false)
    {
        TerminalScreen screen = new(4, rows.Length);
        using BasicVtProcessor source = new(screen);
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), checked((ushort)(onePage ? 1 : rows.Length)));
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                if (onePage)
                {
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture(rows, screen, checked(4 * rows.Length)).WritePayloadTo(output);
                    records.Add(new(tag, output.ToArray()));
                }
                else foreach (TerminalRow row in rows)
                {
                    using MemoryStream output = new();
                    GhosttySnapshotLivePage.Capture([row], screen, 4).WritePayloadTo(output);
                    records.Add(new(tag, output.ToArray()));
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
