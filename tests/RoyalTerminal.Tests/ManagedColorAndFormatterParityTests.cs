// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedColorAndFormatterParityTests
{
    // Ghostty 7cd2f65f5: reset removes an override, so later configuration
    // changes are visible. xterm.js/Windows Terminal also reset to themed colors.
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void DynamicColorOverride_SurvivesThemeChangeUntilReset(int selector)
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        TerminalTheme first = TerminalTheme.Dark.WithDefaultForeground(0xFF123456)
            .WithDefaultBackground(0xFF123456).WithCursorColor(0xFF123456);
        TerminalTheme second = first.WithDefaultForeground(0xFFABCDEF)
            .WithDefaultBackground(0xFFABCDEF).WithCursorColor(0xFFABCDEF);
        processor.ApplyTheme(first);
        Write(processor, $"\x1b]{selector};#445566\x1b\\");
        processor.ApplyTheme(second);
        Assert.Equal(0xFF445566u, DynamicColor(screen, selector));

        // Reset has no semicolon; it must still reach the OSC dispatcher.
        Write(processor, $"\x1b]{selector + 100}\x1b\\");
        Assert.Equal(0xFFABCDEFu, DynamicColor(screen, selector));
        processor.ApplyTheme(first);
        Assert.Equal(0xFF123456u, DynamicColor(screen, selector));
    }

    [Fact]
    public void PaletteOverrides_ResetIndividuallyOrAllAndFollowNewConfiguration()
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        TerminalTheme configured = TerminalTheme.Dark.WithPaletteColor(1, 0xFF123456)
            .WithPaletteColor(2, 0xFFABCDEF);
        Write(processor, "\x1b]4;1;#445566;2;#778899\x1b\\");
        processor.ApplyTheme(configured);
        Assert.Equal(0xFF445566u, screen.Theme.Palette[1]);
        Assert.Equal(0xFF778899u, screen.Theme.Palette[2]);
        Write(processor, "\x1b]104;1\x1b\\");
        Assert.Equal(configured.Palette[1], screen.Theme.Palette[1]);
        Assert.Equal(0xFF778899u, screen.Theme.Palette[2]);
        Write(processor, "\x1b]104\x07");
        Assert.Same(configured.Palette, screen.Theme.Palette);
        processor.ApplyTheme(TerminalTheme.Light);
        Assert.Same(TerminalTheme.Light.Palette, screen.Theme.Palette);
    }

    [Fact]
    public void PaletteInvalidIndexes_DoNotAliasValidEntries()
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        TerminalPalette palette = screen.Theme.Palette;
        Write(processor, "\x1b]4;256;#445566;-1;#778899\x1b\\");
        Assert.Same(palette, screen.Theme.Palette);
    }

    // Ghostty 997a2aff2 reprints the edge cell to preserve pending wrap.
    [Theory]
    [InlineData("abcd")]
    [InlineData("ab界")]
    [InlineData("abca\u0301")]
    public void StyledVt_RoundTripPreservesPendingWrapAndActivePen(string text)
    {
        TerminalScreen sourceScreen = new(4, 3, 0);
        TerminalScreen targetScreen = new(4, 3, 0);
        using BasicVtProcessor source = new(sourceScreen);
        using BasicVtProcessor target = new(targetScreen);
        Write(source, "\x1b[31m" + text + "\x1b[32m");
        TerminalSnapshotExportOptions options = new(TrimTrailingWhitespace: true,
            Extras: new(IncludeCursor: true, IncludeStyle: true, IncludeCharsets: true));
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        Write(target, snapshot);
        Write(source, "X");
        Write(target, "X");
        Assert.Equal(source.CursorRow, target.CursorRow);
        Assert.Equal(source.CursorCol, target.CursorCol);
        Assert.Equal('X', targetScreen.GetViewportRow(1)[0].Codepoint);
        Assert.Equal(sourceScreen.GetViewportRow(1)[0].Foreground, targetScreen.GetViewportRow(1)[0].Foreground);
        for (int column = 0; column < 4; column++)
        {
            Assert.Equal(sourceScreen.GetViewportRow(0)[column].Codepoint, targetScreen.GetViewportRow(0)[column].Codepoint);
            Assert.Equal(sourceScreen.GetViewportRow(0)[column].Grapheme, targetScreen.GetViewportRow(0)[column].Grapheme);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyledVt_CustomTabstopsDoNotOffsetContentAndRestoreRequestedCursor(bool includeCursor)
    {
        TerminalScreen sourceScreen = new(16, 3, 0);
        TerminalScreen targetScreen = new(16, 3, 0);
        using BasicVtProcessor source = new(sourceScreen);
        using BasicVtProcessor target = new(targetScreen);
        Write(source, "\x1b[3g\x1b[5G\x1bH\x1b[12G\x1bH\x1b[Hhello\x1b[2;3H");
        TerminalSnapshotExportOptions options = new(TrimTrailingWhitespace: true,
            Extras: new(IncludeCursor: includeCursor, IncludeTabstops: true));
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        Write(target, snapshot);
        Assert.Equal('h', targetScreen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal(includeCursor ? 1 : 0, target.CursorRow);
        Assert.Equal(includeCursor ? 2 : 0, target.CursorCol);
        Write(target, "\x1b[H\t");
        Assert.Equal(4, target.CursorCol);
        Write(target, "\t");
        Assert.Equal(11, target.CursorCol);
    }

    [Fact]
    public void StyledVt_PartialLinesReplayAtTheStartOfEachRow()
    {
        TerminalScreen sourceScreen = new(8, 3, 0);
        TerminalScreen targetScreen = new(8, 3, 0);
        using BasicVtProcessor source = new(sourceScreen);
        using BasicVtProcessor target = new(targetScreen);
        Write(source, "ab\r\ncd");
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(TrimTrailingWhitespace: true), out string snapshot));
        Write(target, snapshot);
        Assert.Equal('c', targetScreen.GetViewportRow(1)[0].Codepoint);
    }

    private static uint DynamicColor(TerminalScreen screen, int selector) => selector switch
    {
        10 => screen.DefaultForeground,
        11 => screen.DefaultBackground,
        _ => screen.Theme.CursorColor,
    };

    private static void Write(BasicVtProcessor processor, string text) => processor.Process(Encoding.UTF8.GetBytes(text));
}
