// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalWordSelectionTests(ITestOutputHelper output)
{
    private const string Delimiters = " .,\t\r\n";

    public static TheoryData<string, int, int, int, int, int, int> Cases => new()
    {
        { "abcde\r\nfghij", 4, 0, 0, 0, 5, 0 },
        { "abcde\r\nfghij", 0, 1, 0, 1, 5, 1 },
        { "abcdefg", 4, 0, 0, 0, 2, 1 },
        { "abcdefg", 0, 1, 0, 0, 2, 1 },
        { "ab cd", 1, 0, 0, 0, 2, 0 },
        { "a., b", 2, 0, 1, 0, 4, 0 },
        { "a   b", 2, 0, 1, 0, 4, 0 },
        { "ab界", 3, 0, 0, 0, 4, 0 },
        { "abcd界z", 4, 0, 0, 0, 3, 1 },
        { "abcd界z", 1, 1, 0, 0, 3, 1 },
        { "ab 界", 4, 0, 3, 0, 5, 0 },
        { "a🙂b", 2, 0, 0, 0, 4, 0 },
        { "ab\u0301cd", 1, 0, 0, 0, 4, 0 },
        { "ab\u2003cd", 2, 0, 2, 0, 3, 0 },
        { "abcde f", 4, 0, 0, 0, 5, 0 },
        { "abcdefghijkl", 2, 1, 0, 0, 2, 2 },
        { "abcd   z", 0, 1, 4, 0, 2, 1 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ResolvesCellRangesWithoutCrossingHardBreaks(string input, int column, int row,
        int startColumn, int startRow, int endColumn, int endRow)
    {
        TerminalScreen screen = new(5, 4, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes(input));
        Assert.True(TerminalWordSelection.TryResolve(screen, new(column, row), Delimiters, out TerminalWordExtent extent));
        Assert.Equal(new TerminalWordExtent(new(startColumn, startRow), new(endColumn, endRow)), extent);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CellRangesMatchPinnedGhostty(string input, int column, int row,
        int startColumn, int startRow, int endColumn, int endRow)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native word-selection differential unavailable; not counted as native validation.");
            return;
        }
        TerminalScreen screen = new(5, 4, 0);
        using BasicVtProcessor managed = new(screen);
        using GhosttyTerminal native = new(5, 4);
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        managed.Process(bytes); native.Write(bytes);
        Assert.True(TerminalWordSelection.TryResolve(screen, new(column, row), Delimiters, out TerminalWordExtent extent));
        Assert.Equal(new TerminalWordExtent(new(startColumn, startRow), new(endColumn, endRow)), extent);
        Assert.True(native.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Active((ushort)column, (ushort)row), out var reference));
        // Include Unicode whitespace explicitly: native's boundary list is exact,
        // whereas the host retains its existing Unicode-whitespace policy.
        Assert.True(native.TrySelectWord(in reference, [32, 46, 44, 9, 13, 10, 0x2003], out GhosttySelection selection));
        Assert.True(native.TryGetPointFromGridReference(selection.Start, GhosttyVtNative.GhosttyPointTag.Active, out var start));
        Assert.True(native.TryGetPointFromGridReference(selection.End, GhosttyVtNative.GhosttyPointTag.Active, out var end));
        Assert.Equal(extent.Start, new TerminalGridPosition(start.X, (int)start.Y));
        Assert.Equal(extent.End, new TerminalGridPosition(end.X + 1, (int)end.Y));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4)]
    [InlineData(3, 0)]
    public void InvalidOrUnwrittenCellHasNoWord(int column, int row)
    {
        TerminalScreen screen = new(5, 4, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ab"u8);
        Assert.False(TerminalWordSelection.TryResolve(screen, new(column, row), Delimiters, out TerminalWordExtent extent));
        Assert.Equal(default, extent);
    }

    [Theory]
    [InlineData("界")]
    [InlineData("🙂")]
    public void WideDelimiterSelectsItsHeadOwnerAndTail(string glyph)
    {
        TerminalScreen screen = new(5, 4, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("abcd" + glyph + "z"));
        foreach (TerminalGridPosition point in new TerminalGridPosition[] { new(4, 0), new(0, 1), new(1, 1) })
        {
            Assert.True(TerminalWordSelection.TryResolve(screen, point, glyph, out TerminalWordExtent extent));
            Assert.Equal(new TerminalWordExtent(new(4, 0), new(2, 1)), extent);
        }
    }

    [Fact]
    public void OrphanWideSpacersCannotBorrowUnrelatedText()
    {
        TerminalScreen screen = new(5, 2, 0);
        screen.GetViewportRow(0).Cells[4] = new() { Width = 0, IsWideSpacerHead = true };
        screen.GetViewportRow(1).Cells[0] = new() { Width = 1, Codepoint = 'x' };
        screen.GetViewportRow(1).Cells[1] = new() { Width = 0 };
        Assert.False(TerminalWordSelection.TryResolve(screen, new(4, 0), Delimiters, out _));
        Assert.False(TerminalWordSelection.TryResolve(screen, new(1, 1), Delimiters, out _));
    }

    [Fact]
    public void LongWrappedSelectionUsesNoManagedAllocations()
    {
        TerminalScreen screen = new(80, 128, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(new string('a', 80 * 128)));
        for (int index = 0; index < 100; index++)
            TerminalWordSelection.TryResolve(screen, new(40, 64), Delimiters, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        TerminalWordExtent extent = default;
        bool resolved = true;
        for (int index = 0; index < 100; index++)
            resolved &= TerminalWordSelection.TryResolve(screen, new(40, 64), Delimiters, out extent);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(resolved);
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(80, 127)), extent);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ViewportEdgesBoundTheSearchEvenWithHistory()
    {
        TerminalScreen screen = new(5, 2, 100);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abcdefghijklmnopqrst"u8);
        Assert.True(screen.TotalRows > screen.ViewportRows);
        Assert.True(TerminalWordSelection.TryResolve(screen, new(2, 0), Delimiters, out TerminalWordExtent extent));
        Assert.Equal(new TerminalWordExtent(new(0, 0), new(5, 1)), extent);
    }

    [Fact]
    public void NullScreenIsRejected()
        => Assert.Throws<ArgumentNullException>(() => TerminalWordSelection.TryResolve(null!, default, Delimiters, out _));
}
