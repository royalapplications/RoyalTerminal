// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalLineShiftParityTests
{
    [Theory]
    [InlineData('L', false)]
    [InlineData('M', false)]
    [InlineData('L', true)]
    [InlineData('M', true)]
    public void OutsideEitherMarginAxisLineEditsPreservePendingWrap(char operation, bool horizontal)
    {
        TerminalScreen screen = new(8, 5);
        using BasicVtProcessor processor = new(screen);
        Process(processor, OutsideMarginsInput(operation, horizontal));

        AssertPendingWrap(processor, true);
        Assert.Equal((7, 0), (processor.CursorCol, processor.CursorRow));
        Assert.Equal('X', screen.GetViewportRow(0).ReadOnlyCells[7].Codepoint);
        processor.Process("Y"u8);
        int left = horizontal ? 2 : 0;
        Assert.Equal('Y', screen.GetViewportRow(1).ReadOnlyCells[left].Codepoint);
        Assert.Equal((left + 1, 1), (processor.CursorCol, processor.CursorRow));
    }

    [Theory]
    [InlineData('L', false)]
    [InlineData('M', false)]
    [InlineData('L', true)]
    [InlineData('M', true)]
    public void EffectiveLineEditsMoveToLeftMarginAndClearPendingWrap(char operation, bool horizontal)
    {
        TerminalScreen screen = new(8, 5);
        using BasicVtProcessor processor = new(screen);
        string margins = horizontal ? "\u001b[?69h\u001b[3;6s" : "";
        Process(processor, margins + $"\u001b[2;4r\u001b[2;{(horizontal ? 6 : 8)}HX\u001b[2{operation}");

        AssertPendingWrap(processor, false);
        Assert.Equal((horizontal ? 2 : 0, 1), (processor.CursorCol, processor.CursorRow));
        processor.Process("Y"u8);
        Assert.Equal('Y', screen.GetViewportRow(1).ReadOnlyCells[horizontal ? 2 : 0].Codepoint);
    }

    [Theory]
    [InlineData('S')]
    [InlineData('T')]
    public void ScrollPreservesPositionAndPendingWrap(char operation)
    {
        TerminalScreen screen = new(8, 5);
        using BasicVtProcessor processor = new(screen);
        Process(processor, $"\u001b[2;4r\u001b[2;8HX\u001b[2{operation}");
        AssertPendingWrap(processor, true);
        Assert.Equal((7, 1), (processor.CursorCol, processor.CursorRow));
        processor.Process("Y"u8);
        Assert.Equal('Y', screen.GetViewportRow(2).ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData('L', "A\0\0BCF", 0, 1)]
    [InlineData('M', "ADE\0\0F", 0, 1)]
    [InlineData('T', "A\0\0BCF", 3, 5)]
    [InlineData('S', "ADE\0\0F", 3, 5)]
    public void MultiRowOperationsMoveAnchorsAndRasterOnceWithinMargins(char operation, string expected, int cursorColumn, int cursorRow)
    {
        TerminalScreen screen = new(8, 6, 10);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A\u001b[2;1HB\u001b[3;1HC\u001b[4;1HD\u001b[5;1HE\u001b[6;1HF\u001b[2;5r");
        bool down = operation is 'L' or 'T';
        int source = down ? 1 : 4, pruned = down ? 4 : 1;
        TerminalScreenAnchor survivor = screen.CreateAnchor(source, 2);
        TerminalScreenAnchor removed = screen.CreateAnchor(pruned, 2);
        TerminalScreenAnchor status = screen.CreateAnchor(5, 2);
        int rasterId = screen.AllocateRasterImageId();
        screen.ReplaceRasterImage(
            new TerminalRasterImageSource(rasterId, TerminalRasterImageProtocol.Sixel, 1, 1, new byte[4]),
            new TerminalRasterImagePlacement(rasterId, TerminalRasterImageLayer.BelowText,
                2, source, 0, 0, 1, 1, 0, 0, 1, 1, 10, 10));
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, (operation is 'L' or 'M' ? "\u001b[2;4H" : "\u001b[6;4H") + $"\u001b[2{operation}");

        for (int row = 0; row < expected.Length; row++)
            Assert.Equal(expected[row], screen.GetViewportRow(row).ReadOnlyCells[0].Codepoint);
        Assert.Equal(6, screen.TotalRows);
        Assert.Equal((cursorColumn, cursorRow), (processor.CursorCol, processor.CursorRow));
        Assert.True(screen.TryResolveAnchor(survivor, out TerminalGridPosition moved));
        Assert.Equal(source + (down ? 2 : -2), moved.Row);
        Assert.False(screen.TryResolveAnchor(removed, out _));
        Assert.True(screen.TryResolveAnchor(status, out TerminalGridPosition unchanged));
        Assert.Equal(5, unchanged.Row);
        Assert.True(retained.TryResolveAnchor(survivor, out TerminalGridPosition old));
        Assert.Equal(source, old.Row);
        Assert.Equal(moved.Row, Assert.Single(screen.GetRasterImagePlacements().ToArray()).AnchorRow);
        Assert.Equal(source, Assert.Single(retained.GetRasterImagePlacements().ToArray()).AnchorRow);
    }

    [Theory]
    [InlineData('L', false)]
    [InlineData('M', false)]
    [InlineData('L', true)]
    [InlineData('M', true)]
    public void OutsideMarginContinuationMatchesNative(char operation, bool horizontal)
    {
        RequireNative();
        using BasicVtProcessor seed = new(new TerminalScreen(8, 5));
        byte[] snapshot = seed.GetBinarySnapshot();
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        foreach (string input in new[] { OutsideMarginsInput(operation, horizontal), "Y" })
        {
            byte[] bytes = Encoding.ASCII.GetBytes(input);
            native.Write(bytes);
            managed.Processor.Process(bytes);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
            AssertPendingWrap(managed.Processor, expected.State.PendingWrap);
            Assert.Equal(expected.State.CursorX, managed.Processor.CursorCol);
            Assert.Equal(expected.State.CursorY, managed.Processor.CursorRow);
            int row = 0;
            foreach (GhosttySnapshotPage page in expected.Pages)
            foreach (TerminalRow decoded in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(8, 1)))
            {
                for (int column = 0; column < 8; column++)
                    Assert.Equal(decoded.ReadOnlyCells[column].Codepoint, managed.Screen.GetViewportRow(row).ReadOnlyCells[column].Codepoint);
                row++;
            }
        }
    }

    private static string OutsideMarginsInput(char operation, bool horizontal)
        => (horizontal ? "\u001b[?69h\u001b[3;6s" : "\u001b[2;4r") + $"\u001b[1;8HX\u001b[2{operation}";

    private static void AssertPendingWrap(BasicVtProcessor processor, bool pending)
    {
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        Assert.Equal(pending, reader.ReadReady().Screens[0].State.PendingWrap);
    }

    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
