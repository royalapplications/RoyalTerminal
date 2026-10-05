// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSnapshotColorTests(ITestOutputHelper output)
{
    // Ghostty keeps nullable defaults and sparse override identity in TERMINAL.
    // WT and xterm.js also restore configured colors, not the last rendered value.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredColorsFollowNativeAcrossResetsAndConfiguration(bool configured)
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 2);
        TerminalTheme host = TerminalTheme.Dark.WithDefaultForeground(0xFF123456)
            .WithDefaultBackground(0xFF234567).WithCursorColor(0xFF345678);
        if (configured) ConfigureNative(native, host);
        native.Write("\u001b]10;#000000\u001b\\\u001b]11;#abcdef\u001b\\\u001b]12;#123456\u001b\\"u8);
        // Force equality to an original palette entry: the mask must still survive.
        GhosttySnapshotTerminalState initial = Read(native);
        native.Write(Encoding.ASCII.GetBytes($"\u001b]4;0;#{initial.OriginalPaletteColor(0):x6};255;#aabbcc\u001b\\"));
        GhosttySnapshotTerminalState source = Read(native);
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor managed = new(screen);
        managed.InstallSnapshotColors(source, host);
        Compare(native, managed);
        Assert.True(managed.SnapshotColors.HasPaletteOverride(0));
        Assert.Equal(host.SelectionBackground, screen.Theme.SelectionBackground);
        foreach (string command in new[] { "\u001b]110\u001b\\", "\u001b]111\u001b\\", "\u001b]112\u001b\\",
            "\u001b]104;0\u001b\\", "\u001b]104\u001b\\", "\u001b]10;#112233\u001b\\", "\u001b]4;127;#445566\u001b\\" })
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes);
            // Exercise continuation through every byte of a post-install command.
            foreach (byte value in bytes) managed.Process(new ReadOnlySpan<byte>(in value));
            Compare(native, managed);
        }
        TerminalTheme next = TerminalTheme.Light.WithPaletteColor(127, 0xFF778899);
        ConfigureNative(native, next); managed.ApplyTheme(next);
        Compare(native, managed);
        native.Write("\u001b]110\u001b\\\u001b]104\u001b\\"u8);
        managed.Process("\u001b]110\u001b\\\u001b]104\u001b\\"u8);
        Compare(native, managed);
        Assert.Equal(next.DefaultForeground, screen.DefaultForeground);
        Assert.Equal(next.Palette[127], screen.Theme.Palette[127]);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void AbsentDefaultsUseHostForRenderingButNotQueries(int selector)
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 2);
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor managed = new(screen);
        managed.InstallSnapshotColors(Read(native), TerminalTheme.Light);
        List<byte> response = [];
        managed.ResponseCallback = bytes => response.AddRange(bytes.ToArray());
        managed.Process(Encoding.ASCII.GetBytes($"\u001b]{selector};?\u001b\\"));
        Assert.Empty(response);
        Assert.Equal(TerminalTheme.Light.DefaultForeground, screen.DefaultForeground);
        Assert.Equal(new GhosttySnapshotDynamicColor(null, null), managed.SnapshotColors.GetSnapshotDynamic(selector));
        managed.Process(Encoding.ASCII.GetBytes($"\u001b]{selector};#000000\u001b\\\u001b]{selector};?\u001b\\"));
        Assert.NotEmpty(response);
        Assert.Equal(0u, managed.SnapshotColors.GetSnapshotDynamic(selector).Override);
        response.Clear();
        managed.Process(Encoding.ASCII.GetBytes($"\u001b]{selector + 100}\u001b\\\u001b]{selector};?\u001b\\"));
        Assert.Empty(response);
    }

    [Fact]
    public void AbsentCursorQueryFallsBackToForegroundWithoutChangingSnapshotState()
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 2);
        native.SetForegroundColor(Rgb(0x00123456));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 2));
        managed.InstallSnapshotColors(Read(native), TerminalTheme.Dark);
        List<byte> response = [];
        managed.ResponseCallback = bytes => response.AddRange(bytes.ToArray());
        managed.Process("\u001b]12;?\u001b\\"u8);
        Assert.NotEmpty(response);
        Assert.Equal(new GhosttySnapshotDynamicColor(null, null), managed.SnapshotColors.GetSnapshotDynamic(12));
        Assert.Contains("12;rgb:", Encoding.ASCII.GetString(response.ToArray()));
    }

    [Fact]
    public void InstallingColorsRecolorsLogicalCellsAndPreservesTruecolor()
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshotFramingTests.Fixture("complete-v1.hex"), new());
        GhosttySnapshotTerminalState source = reader.ReadReady().Terminal;
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor managed = new(screen);
        managed.Process("A\u001b[31mB\u001b[38;2;17;34;51mC"u8);
        managed.InstallSnapshotColors(source, TerminalTheme.Light);
        Assert.Equal(screen.DefaultForeground, screen.GetViewportRow(0)[0].Foreground);
        Assert.Equal(0xFF000000 | source.CurrentPaletteColor(1), screen.GetViewportRow(0)[1].Foreground);
        Assert.Equal(0xFF112233u, screen.GetViewportRow(0)[2].Foreground);
        managed.Process("D"u8);
        Assert.Equal(0xFF112233u, screen.GetViewportRow(0)[3].Foreground);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot color differential available: {available}");
        return available;
    }

    private static GhosttySnapshotTerminalState Read(GhosttyTerminal native)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        return reader.ReadReady().Terminal;
    }

    private static void Compare(GhosttyTerminal native, BasicVtProcessor managed)
    {
        GhosttySnapshotTerminalState expected = Read(native);
        ManagedTerminalColors actual = managed.SnapshotColors;
        Assert.Equal(expected.Header.Foreground, actual.GetSnapshotDynamic(10));
        Assert.Equal(expected.Header.Background, actual.GetSnapshotDynamic(11));
        Assert.Equal(expected.Header.CursorColor, actual.GetSnapshotDynamic(12));
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal(expected.OriginalPaletteColor(i), actual.GetOriginalPalette(i) & 0xFFFFFF);
            Assert.Equal(expected.CurrentPaletteColor(i), actual.GetPalette(i) & 0xFFFFFF);
            Assert.Equal(expected.HasPaletteOverride(i), actual.HasPaletteOverride(i));
        }
    }

    private static void ConfigureNative(GhosttyTerminal native, TerminalTheme theme)
    {
        native.SetForegroundColor(Rgb(theme.DefaultForeground));
        native.SetBackgroundColor(Rgb(theme.DefaultBackground));
        native.SetCursorColor(Rgb(theme.CursorColor));
        GhosttyVtNative.GhosttyColorRgb[] palette = new GhosttyVtNative.GhosttyColorRgb[256];
        for (int i = 0; i < palette.Length; i++) palette[i] = Rgb(theme.Palette[i]);
        native.SetPalette(palette);
    }

    private static GhosttyVtNative.GhosttyColorRgb Rgb(uint color)
        => new() { R = (byte)(color >> 16), G = (byte)(color >> 8), B = (byte)color };
}
