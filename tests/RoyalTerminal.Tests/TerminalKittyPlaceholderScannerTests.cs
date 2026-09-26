// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalKittyPlaceholderScannerTests
{
    private const string P = "\U0010EEEE";

    [Fact]
    public void DiacriticTableSurvivesCompactingCollections()
    {
        // Load the scanner without accessing the lazily initialized table,
        // then promote existing objects before its first diacritic lookup.
        Assert.Equal(0u, Scan(new TerminalCell[1]));
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        int[] expected = CopyDiacritics();
        for (int generation = 0; generation < 8; generation++)
        {
            GC.Collect(generation % (GC.MaxGeneration + 1), GCCollectionMode.Forced, blocking: true, compacting: true);
            // Keep the expected values in a separate array, not a span that
            // would itself root the table and hide a static lifetime problem.
            Assert.Equal(expected, CopyDiacritics());
            TerminalKittyPlaceholderScanner scanner = new([Cell("\u0305\u030D\u030E")]);
            Assert.True(scanner.TryReadNext(out TerminalKittyPlaceholderRun run));
            Assert.Equal(new(0, 33554474, 0, 1, 0, 1), run);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int[] CopyDiacritics() => TerminalKittyPlaceholderScanner.Diacritics.ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParsedCellsRetainPaletteIdsAndHighBitsAcrossCompatibleRuns(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(12, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[?2027h\u001b[38;5;42;58;5;21m" +
            "_" + P + "\u0305\u030D\u030E" + P + "\u0305\u030E" + P + "_" + P));
        TerminalKittyPlaceholderScanner scanner = new(screen.GetViewportRow(0).ReadOnlyCells);
        Assert.True(scanner.TryReadNext(out TerminalKittyPlaceholderRun run));
        Assert.Equal(new(1, 33554474, 21, 1, 0, 3), run);
        Assert.True(scanner.TryReadNext(out run));
        Assert.Equal(new(5, 42, 21, 0, 0, 1), run);
        Assert.False(scanner.TryReadNext(out _));
    }

    [Theory]
    [InlineData("", "", 1)]
    [InlineData("\u030D\u030E", "\u030D\u0310", 1)]
    [InlineData("\u030D\u030E", "\u030D", 1)]
    [InlineData("\u030D\u030E", "", 1)]
    [InlineData("\u030D\u030E", "\u0300\u0300", 1)]
    [InlineData("\u030D\u030E", "\u030E", 2)]
    [InlineData("\u030D\u030E", "\u030D\u030E", 2)]
    [InlineData("\u0305\u0305\u030E", "\u0305\u030D\u030D", 2)]
    [InlineData("\u0305\u0305", "\u0305\u030D\u0305", 2)]
    public void ContinuationMatchesGhosttyNullableDiacriticRules(string first, string second, int expectedRuns)
    {
        TerminalCell[] cells = [Cell(first), Cell(second)];
        TerminalKittyPlaceholderScanner scanner = new(cells);
        Assert.True(scanner.TryReadNext(out TerminalKittyPlaceholderRun run));
        Assert.Equal(expectedRuns == 1 ? 2u : 1u, run.Width);
        Assert.Equal(expectedRuns == 2, scanner.TryReadNext(out _));
        Assert.False(scanner.TryReadNext(out _));
    }

    [Fact]
    public void ZeroUnderlineIdentityMeansNoPlacementAndChangedIdsSplitRuns()
    {
        TerminalCell first = Cell("");
        TerminalCell second = Cell("");
        second.UnderlineIdentity = TerminalColorIdentity.Rgb(0);
        TerminalCell third = Cell("");
        third.UnderlineIdentity = TerminalColorIdentity.Palette(1);
        TerminalCell fourth = Cell("");
        fourth.ForegroundIdentity = TerminalColorIdentity.Rgb(43);
        TerminalKittyPlaceholderScanner scanner = new([first, second, third, fourth]);
        Assert.True(scanner.TryReadNext(out TerminalKittyPlaceholderRun run));
        Assert.Equal(2u, run.Width);
        Assert.Equal(0u, run.PlacementId);
        Assert.True(scanner.TryReadNext(out run));
        Assert.Equal(1u, run.PlacementId);
        Assert.True(scanner.TryReadNext(out run));
        Assert.Equal(43u, run.ImageId);
        Assert.False(scanner.TryReadNext(out _));
    }

    [Fact]
    public void EveryDiacriticDecodesItsIndexIncludingSupplementaryCodepoints()
    {
        ReadOnlySpan<int> table = TerminalKittyPlaceholderScanner.Diacritics;
        Assert.Equal(297, table.Length);
        for (int i = 0; i < table.Length; i++)
        {
            if (i > 0) Assert.True(table[i] > table[i - 1]);
            string mark = char.ConvertFromUtf32(table[i]);
            TerminalKittyPlaceholderScanner scanner = new([Cell(mark + mark + mark)]);
            Assert.True(scanner.TryReadNext(out TerminalKittyPlaceholderRun run));
            Assert.Equal((uint)i, run.ImageRow);
            Assert.Equal((uint)i, run.ImageColumn);
            Assert.Equal(42u | (i < 256 ? (uint)i << 24 : 0), run.ImageId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u0305")]
    [InlineData("\u0305\u030D\u030E")]
    public void WarmScansDoNotAllocate(string suffix)
    {
        TerminalCell[] cells = new TerminalCell[80];
        Array.Fill(cells, Cell(suffix));
        for (int i = 0; i < 100; i++) Scan(cells);
        long before = GC.GetAllocatedBytesForCurrentThread();
        uint width = 0;
        for (int i = 0; i < 1_000; i++) width += Scan(cells);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(80_000u, width);
        Assert.Equal(0, allocated);
    }

    private static uint Scan(TerminalCell[] cells)
    {
        TerminalKittyPlaceholderScanner scanner = new(cells);
        uint total = 0;
        while (scanner.TryReadNext(out TerminalKittyPlaceholderRun run)) total += run.Width;
        return total;
    }

    private static TerminalCell Cell(string suffix) => new()
    {
        Codepoint = TerminalKittyPlaceholderScanner.Placeholder,
        Grapheme = P + suffix,
        ForegroundIdentity = TerminalColorIdentity.Palette(42),
        Width = 1,
    };
}
