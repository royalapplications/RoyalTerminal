// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedSnapshotStyleEditTests
{
    // Ghostty Terminal.insertLines/deleteLines clamp and traverse once. WT's
    // _InsertDeleteLineHelper likewise scrolls a bounded rectangle. xterm.js
    // supplies the visible IL/DL reference but repeats splices; Ghostty defines
    // PAGE copy/growth order and the out-of-margin pending-wrap contract here.
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    [InlineData(true, 65535)]
    [InlineData(false, 65535)]
    public void WholeRegionLineEditsClearWithoutCopyingDiscardedStyles(bool insert, int count)
    {
        TerminalRow rich = StyledRow(CellAttributes.Italic, CellAttributes.Underline,
            CellAttributes.Strikethrough, CellAttributes.Italic);
        TerminalRow small = StyledRow(CellAttributes.Bold, CellAttributes.Dim, CellAttributes.Bold, CellAttributes.Dim);
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(insert ? [rich, small] : [small, rich]));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        int destination = insert ? 1 : 0;
        GhosttySnapshotPageAllocation before = terminal.Screen.GetViewportRow(destination).SnapshotAllocation!;

        terminal.Processor.Process(Encoding.ASCII.GetBytes($"\u001b[{count}{(insert ? 'L' : 'M')}"));

        Assert.Same(before, terminal.Screen.GetViewportRow(destination).SnapshotAllocation);
        Assert.Equal(4, Capacity(terminal.Screen, destination));
        for (int row = 0; row < 2; row++)
        {
            foreach (TerminalCell cell in terminal.Screen.GetViewportRow(row).ReadOnlyCells)
            {
                Assert.Equal(0, cell.Codepoint);
                Assert.Equal(CellAttributes.None, cell.Attributes);
            }
            Assert.Equal('A', retained.GetViewportRow(row).ReadOnlyCells[0].Codepoint);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultiLineCopySkipsIntermediateDiscardedMetadata(bool insert)
    {
        byte[] snapshot = SkippedMetadataSnapshot(insert);
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(snapshot);
        terminal.Processor.Process(Encoding.ASCII.GetBytes(insert ? "\u001b[2L" : "\u001b[2M"));

        int destination = insert ? 2 : 0;
        Assert.Equal(4, Capacity(terminal.Screen, destination));
        Assert.Equal('A', terminal.Screen.GetViewportRow(destination).ReadOnlyCells[0].Codepoint);
        Assert.Equal(CellAttributes.None, terminal.Screen.GetViewportRow(destination).ReadOnlyCells[0].Attributes);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void MultiLineInPageMovesRetainCowStorageAndSemanticOwnership(bool insert, bool wide)
    {
        TerminalRow[] rows = [StyledRow(CellAttributes.Bold, CellAttributes.Bold, CellAttributes.Bold, CellAttributes.Bold),
            StyledRow(CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic),
            StyledRow(CellAttributes.Bold, CellAttributes.Bold, CellAttributes.Bold, CellAttributes.Bold),
            StyledRow(CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic, CellAttributes.Italic)];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index][0].Codepoint = 'A' + index;
            rows[index].SemanticPrompt = index % 2 == 0 ? TerminalSemanticPrompt.Prompt : TerminalSemanticPrompt.None;
            rows[index].WrapsToNext = index != rows.Length - 1;
            rows[index].IsWrapContinuation = index != 0;
            if (wide)
            {
                rows[index][1].Codepoint = '界';
                rows[index][1].Width = 2;
                rows[index][2].Codepoint = 0;
                rows[index][2].Width = 0;
            }
        }
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(Snapshot(rows, onePage: true));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.Process(Encoding.ASCII.GetBytes(insert ? "\u001b[2L" : "\u001b[2M"));

        for (int row = 0; row < rows.Length; row++)
        {
            TerminalRow actual = terminal.Screen.GetViewportRow(row);
            int source = row + (insert ? -2 : 2);
            if (source >= 0 && source < rows.Length)
            {
                Assert.Same(retained.GetViewportRow(source).SearchStorageIdentity, actual.SearchStorageIdentity);
                Assert.Equal('A' + source, actual.ReadOnlyCells[0].Codepoint);
                Assert.Equal(rows[source].SemanticPrompt, actual.SemanticPrompt);
            }
            else
            {
                Assert.Equal(0, actual.ReadOnlyCells[0].Codepoint);
                Assert.Equal(TerminalSemanticPrompt.None, actual.SemanticPrompt);
            }
            Assert.False(actual.WrapsToNext);
            Assert.False(actual.IsWrapContinuation);
            Assert.Equal('A' + row, retained.GetViewportRow(row).ReadOnlyCells[0].Codepoint);
            Assert.Equal(rows[row].WrapsToNext, retained.GetViewportRow(row).WrapsToNext);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void BoundedMultiLineMetadataOrderMatchesNative(bool insert, bool scroll)
    {
        RequireNative();
        // A top margin prevents SU from taking the history-producing path.
        byte[] snapshot = SkippedMetadataSnapshot(insert, statusRow: scroll);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        string setup = scroll ? "\u001b[2;4r" : "";
        char operation = scroll ? (insert ? 'T' : 'S') : (insert ? 'L' : 'M');
        foreach (string input in new[] { setup + $"\u001b[2{operation}", $"\u001b[65535{operation}" })
        {
            byte[] bytes = Encoding.ASCII.GetBytes(input);
            native.Write(bytes);
            managed.Processor.Process(bytes);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
            int index = 0;
            foreach (GhosttySnapshotPage page in expected.Pages)
            foreach (TerminalRow row in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(4, 1)))
            {
                TerminalRow actual = managed.Screen.GetViewportRow(index);
                Assert.Equal(page.Capacity.Styles, Capacity(managed.Screen, index));
                Assert.Equal(row.SemanticPrompt, actual.SemanticPrompt);
                Assert.Equal(row.WrapsToNext, actual.WrapsToNext);
                Assert.Equal(row.IsWrapContinuation, actual.IsWrapContinuation);
                for (int column = 0; column < 4; column++)
                {
                    TerminalCell cell = row.ReadOnlyCells[column], observed = actual.ReadOnlyCells[column];
                    Assert.Equal(cell.Codepoint, observed.Codepoint);
                    Assert.Equal(GhosttySnapshotLivePage.EncodeStyle(in cell), GhosttySnapshotLivePage.EncodeStyle(in observed));
                }
                index++;
            }
            Assert.Equal(managed.Screen.ViewportRows, index);
            Assert.Equal(expected.State.CursorX, managed.Processor.CursorCol);
            Assert.Equal(expected.State.CursorY, managed.Processor.CursorRow);
        }
    }

    private static byte[] SkippedMetadataSnapshot(bool insert, bool statusRow = false)
    {
        TerminalRow plain = StyledRow(CellAttributes.None, CellAttributes.None, CellAttributes.None, CellAttributes.None);
        TerminalRow rich = StyledRow(CellAttributes.Italic, CellAttributes.Underline, CellAttributes.Strikethrough, CellAttributes.Italic);
        TerminalRow small = StyledRow(CellAttributes.Bold, CellAttributes.Dim, CellAttributes.Bold, CellAttributes.Dim);
        TerminalRow[] rows = insert ? [plain, rich, small] : [small, rich, plain];
        return Snapshot(statusRow ? [StyledRow(CellAttributes.None, CellAttributes.None, CellAttributes.None, CellAttributes.None), .. rows] : rows);
    }
}
