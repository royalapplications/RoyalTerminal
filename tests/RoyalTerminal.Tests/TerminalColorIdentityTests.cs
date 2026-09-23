// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalColorIdentityTests
{
    [Fact]
    public void RepresentationIsCompactAndDistinguishesDefaultPaletteAndRgbBlack()
    {
        Assert.Equal(4, Unsafe.SizeOf<TerminalColorIdentity>());
        // Keep the richer cell within the previous 64-bit storage budget by grouping fields.
        Assert.True(Unsafe.SizeOf<TerminalCell>() <= 48, $"Cell size: {Unsafe.SizeOf<TerminalCell>()}");
        TerminalColorIdentity defaultColor = default;
        Assert.Equal(TerminalColorKind.Default, defaultColor.Kind);
        Assert.NotEqual(defaultColor, TerminalColorIdentity.Palette(0));
        Assert.NotEqual(defaultColor, TerminalColorIdentity.Rgb(0));
        Assert.NotEqual(TerminalColorIdentity.Palette(0), TerminalColorIdentity.Rgb(0));
        Assert.Equal(TerminalColorIdentity.Rgb(0x123456), TerminalColorIdentity.Rgb(0xFF123456));
        Assert.Equal(0x123456u, TerminalColorIdentity.Rgb(0x123456).Value);
        Assert.Equal(255u, TerminalColorIdentity.Palette(255).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessorPreservesColorRepresentationsIncludingZero(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(10, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("A\u001b[38;5;0;48;5;7;58;5;3mB\u001b[38;2;0;0;0;48;2;1;2;3;58;2;4;5;6mC\u001b[39;49;59mD"u8);
        Assert.Equal(default, screen.GetViewportRow(0)[0].ForegroundIdentity);
        AssertColors(screen.GetViewportRow(0)[1], TerminalColorIdentity.Palette(0), TerminalColorIdentity.Palette(7), TerminalColorIdentity.Palette(3));
        AssertColors(screen.GetViewportRow(0)[2], TerminalColorIdentity.Rgb(0), TerminalColorIdentity.Rgb(0x010203), TerminalColorIdentity.Rgb(0x040506));
        AssertColors(screen.GetViewportRow(0)[3], default, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedCursorRestoresColorsAndErasureKeepsOnlyBackgroundIdentity(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(10, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("\u001b[38;5;9;48;5;7;58;5;3m\u001b7\u001b[0m\u001b8X\u001b[K"u8);
        AssertColors(screen.GetViewportRow(0)[0], TerminalColorIdentity.Palette(9), TerminalColorIdentity.Palette(7), TerminalColorIdentity.Palette(3));
        AssertColors(screen.GetViewportRow(0)[1], default, TerminalColorIdentity.Palette(7), default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideCellsAndHeldScreenKeepIndependentLogicalColors(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(10, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[38;5;9;58;2;1;2;3m界\u001b[?2026h\r\u001b[0mAB"));
        Assert.Equal(TerminalColorIdentity.Palette(9), screen.GetViewportRow(0)[0].ForegroundIdentity);
        // Native spacer tails have no style of their own; managed wide spacers retain their leader's style.
        if (!native) Assert.Equal(TerminalColorIdentity.Rgb(0x010203), screen.GetViewportRow(0)[1].UnderlineIdentity);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(default, screen.GetViewportRow(0)[0].ForegroundIdentity);
        Assert.Equal(default, screen.GetViewportRow(0)[1].UnderlineIdentity);
    }

    [Fact]
    public void ReflowPreservesIdentityAndNormalizesWideSpacers()
    {
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[38;5;9;48;2;1;2;3;58;5;2mAB界CD"));
        screen.GetRow(0)[3].ForegroundIdentity = default; // Force the scalar spacer-normalization path.
        screen.Resize(4, 3);
        screen.Resize(8, 3);
        for (int column = 0; column < 6; column++)
            AssertColors(screen.GetRow(0)[column], TerminalColorIdentity.Palette(9), TerminalColorIdentity.Rgb(0x010203), TerminalColorIdentity.Palette(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollBlankRetainsCurrentBackgroundIdentity(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(10, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("\u001b[48;5;7m\u001b[3;1H\n"u8);
        Assert.Equal(TerminalColorIdentity.Palette(7), screen.GetViewportRow(2)[0].BackgroundIdentity);
        Assert.Equal(screen.Theme.Palette[7], screen.GetViewportRow(2)[0].Background);
    }

    [Fact]
    public void ThemeChangeDoesNotRewriteExplicitRgbPenOrItsSavedIdentity()
    {
        TerminalScreen screen = new(10, 3);
        using BasicVtProcessor processor = new(screen);
        uint color = screen.Theme.Palette[1];
        string sgr = $"\u001b[38;2;{(color >> 16) & 255};{(color >> 8) & 255};{color & 255}m";
        processor.Process(Encoding.ASCII.GetBytes(sgr + "\u001b7"));
        processor.Process("\u001b]4;1;#010203\u001b\\X\u001b[0m\u001b8Y"u8);
        Assert.Equal(color, screen.GetViewportRow(0)[0].Foreground);
        Assert.Equal(TerminalColorIdentity.Rgb(color), screen.GetViewportRow(0)[0].ForegroundIdentity);
    }

    private static void AssertColors(TerminalCell cell, TerminalColorIdentity foreground,
        TerminalColorIdentity background, TerminalColorIdentity underline)
    {
        Assert.Equal(foreground, cell.ForegroundIdentity);
        Assert.Equal(background, cell.BackgroundIdentity);
        Assert.Equal(underline, cell.UnderlineIdentity);
    }
}
