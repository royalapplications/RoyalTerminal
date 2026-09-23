// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalCellThemeParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalResolvedColorsRetainTheirDistinctMeaningOnThemeChange(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(12, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        ITerminalThemeSink themes = (ITerminalThemeSink)processor;
        TerminalTheme first = TerminalTheme.Dark.WithDefaultForeground(0xFF123456)
            .WithDefaultBackground(0xFF123456).WithPaletteColor(1, 0xFF123456)
            .WithPaletteColor(2, 0xFF123456);
        themes.ApplyTheme(first);
        processor.Process("A\u001b[38;5;1;48;5;1;58;5;1mB\u001b[38;5;2;48;5;2;58;5;2mC\u001b[38;2;18;52;86;48;2;18;52;86;58;2;18;52;86mD"u8);
        TerminalTheme next = first.WithDefaultForeground(0xFF010203).WithDefaultBackground(0xFF040506)
            .WithPaletteColor(1, 0xFF070809).WithPaletteColor(2, 0xFF0A0B0C);
        themes.ApplyTheme(next);
        ReadOnlySpan<TerminalCell> cells = screen.GetViewportRow(0).ReadOnlyCells;
        Assert.Equal((next.DefaultForeground, next.DefaultBackground), (cells[0].Foreground, cells[0].Background));
        for (int i = 1; i <= 2; i++)
        {
            Assert.Equal((next.Palette[i], next.Palette[i], next.Palette[i]),
                (cells[i].Foreground, cells[i].Background, cells[i].UnderlineColor));
            Assert.Equal(TerminalColorIdentity.Palette((byte)i), cells[i].ForegroundIdentity);
        }
        Assert.Equal((0xFF123456u, 0xFF123456u, 0xFF123456u),
            (cells[3].Foreground, cells[3].Background, cells[3].UnderlineColor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OscPaletteUpdatesAndResetsRecolorExistingCellsAndErasedBackground(bool native)
    {
        if (!CanRun(native)) return;
        TerminalScreen screen = new(12, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        uint original = screen.Theme.Palette[1];
        processor.Process("\u001b[38;5;1;48;5;1;58;5;1mX\u001b[K\u001b]4;1;#123456\u001b\\"u8);
        TerminalCell cell = screen.GetViewportRow(0).ReadOnlyCells[0];
        Assert.Equal((0xFF123456u, 0xFF123456u, 0xFF123456u), (cell.Foreground, cell.Background, cell.UnderlineColor));
        Assert.Equal(0xFF123456u, screen.GetViewportRow(0).ReadOnlyCells[1].Background);
        processor.Process("\u001b]104;1\u001b\\"u8);
        cell = screen.GetViewportRow(0).ReadOnlyCells[0];
        Assert.Equal((original, original, original), (cell.Foreground, cell.Background, cell.UnderlineColor));
        Assert.Equal(original, screen.GetViewportRow(0).ReadOnlyCells[1].Background);
    }

    [Fact]
    public void ThemeResolutionPreservesSharedSnapshotsAndHiddenHistoryAndAlternateCells()
    {
        TerminalScreen screen = new(12, 3, 20);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[38;5;1;48;5;2;58;5;3mABCDEFGHIJK\r\nline\r\nline\r\n"u8);
        TerminalScreen frozen = screen.CreateStateCopy();
        TerminalCell before = frozen.GetRow(0).ReadOnlyCells[10];
        screen.Resize(6, 3, reflowOnResize: false);
        processor.Process("\u001b[?1049h\u001b[38;5;1mZ"u8);
        TerminalTheme next = screen.Theme.WithPaletteColor(1, 0xFF010203)
            .WithPaletteColor(2, 0xFF040506).WithPaletteColor(3, 0xFF070809);
        processor.ApplyTheme(next);
        Assert.Equal(next.Palette[1], screen.GetViewportRow(0).ReadOnlyCells[0].Foreground);
        processor.Process("\u001b[?1049l"u8);
        screen.Resize(12, 3, reflowOnResize: false);
        TerminalCell after = screen.GetRow(0).ReadOnlyCells[10];
        Assert.Equal('K', after.Codepoint);
        Assert.Equal((next.Palette[1], next.Palette[2], next.Palette[3]),
            (after.Foreground, after.Background, after.UnderlineColor));
        Assert.Equal(before, frozen.GetRow(0).ReadOnlyCells[10]);
    }

    [Fact]
    public void UnchangedResolutionDoesNotAllocateOrDetachSharedRows()
    {
        TerminalScreen screen = new(80, 24);
        TerminalScreen copy = screen.CreateStateCopy();
        TerminalTheme theme = screen.Theme;
        copy.ApplyTheme(theme, invalidateRows: false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) copy.ApplyTheme(theme, invalidateRows: false);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        // The first changed palette/default theme must still detach before mutation.
        copy.ApplyTheme(theme.WithDefaultForeground(0xFF123456));
        Assert.Equal(theme.DefaultForeground, screen.GetViewportRow(0).ReadOnlyCells[0].Foreground);
    }

    private bool CanRun(bool native)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native requested: {native}; available: {available}");
        return !native || available;
    }

    [Fact]
    public void UnusedPaletteChangesDoNotDetachOrDirtySharedRowStorage()
    {
        TerminalScreen screen = new(80, 24);
        TerminalTheme theme = screen.Theme.WithPaletteColor(42, 0xFF123456);
        TerminalRow row = screen.GetViewportRow(0);
        row.ResolveCellColors(theme); // Warm up before creating a fresh shared row.
        TerminalRow copy = row.CreateStateCopy();
        copy.IsDirty = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool changed = copy.ResolveCellColors(theme);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(changed);
        Assert.False(copy.IsDirty);
        Assert.Equal(0, allocated);
    }
}
