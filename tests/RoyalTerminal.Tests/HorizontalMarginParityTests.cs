// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class HorizontalMarginParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001b[?6hABCDxy")]
    [InlineData("\u001b[?6h\u001b[2;2H\rQ")]
    [InlineData("\u001b[?6h\u001b[99;99H!")]
    [InlineData("\u001b[3;4H\u001b[99CZ\u001b[99DX")]
    [InlineData("\u001b[1;1H\bQ")]
    [InlineData("\u001b[3;8H\rQ")]
    [InlineData("\u001b[?69l\u001b[3;8H\rQ")]
    [InlineData("\u001b[2S")]
    [InlineData("\u001b[2T")]
    [InlineData("\u001b[4;4H\n")]
    [InlineData("\u001b[4;1H\n")]
    [InlineData("\u001b[5;4H\n")]
    [InlineData("\u001b[2;4H\u001bM")]
    [InlineData("\u001b[2;1H\u001bM")]
    [InlineData("\u001b[3;4H\u001b[2L")]
    [InlineData("\u001b[3;4H\u001b[2M")]
    [InlineData("\u001b[3;1H\u001b[2L")]
    [InlineData("\u001b[3;4H\u001b[2@")]
    [InlineData("\u001b[3;4H\u001b[2P")]
    [InlineData("\u001b[3;8H\u001b[2P")]
    [InlineData("\u001b[3;4H\u001b[99X")]
    [InlineData("\u001b[3;4H\u001b[9;2sQ")]
    [InlineData("\u001b[3;1H\u001b[2@")]
    [InlineData("\u001b[3;1H\u001b[2P")]
    [InlineData("\u001b[3;4H\tQ")]
    [InlineData("\u001b[3;6H\u001b[ZQ")]
    [InlineData("\u001b[3;2H界\u001b[2S")]
    [InlineData("\u001b[3;6H界\u001b[2T")]
    [InlineData("\u001b[?6h界界界界界界界")]
    [InlineData("\u001b[?6h\u001b[99C界A")]
    [InlineData("\u001b[3;8Hxyz")]
    [InlineData("\u001bc\u001b[3;8H\rQ")]
    [InlineData("\u001b[?1049h\u001b[3;8H\rQ")]
    [InlineData("\u001b[?1049h\u001b[?1049l\u001b[3;8H\rQ")]
    [InlineData("\u001b[?6h\u001b[2;2H\u001b[6n\u001bP$qs\u001b\\")]
    [InlineData("\u001b[?69l\u001bP$qs\u001b\\")]
    [InlineData("\u001b[?69l\u001b[3;4H\u001b[2;5s\u001b[1;1H\u001b[uQ")]
    [InlineData("\u001b[?6h\u001b[99I\u001b[99ZQ")]
    [InlineData("\u001b[3;8H\tQ")]
    [InlineData("\u001b[?6h\u001b[99C❤\ufe0fA")]
    [InlineData("\u001b[?6h\u001b[99C界\ufe0eA")]
    [InlineData("\u001b[?6h\u001b[?7l\u001b[99C❤\ufe0fA")]
    [InlineData("\u001b[3;5H界\u001b[3;4H\u001b[@")]
    [InlineData("\u001b[3;4H界\u001b[3;4H\u001b[P")]
    [InlineData("\u001b[3;4H界\u001b[3;5H\u001b[P")]
    [InlineData("\u001b[?6hABCD\u001b[SX")]
    [InlineData("\u001b[?6hABCD\u001b[TX")]
    [InlineData("\u001b[20h\u001b[4;8H\nQ")]
    [InlineData("\u001b[?6hABCD\u001b[6;3sX")]
    [InlineData("\u001b[?6hABCD\u001b[?69lX")]
    [InlineData("\u001b[?69l\u001b[2;1Habcdefg界\u001b[?69h\u001b[3;6s\u001b[S")]
    [InlineData("\u001b[48;2;10;20;30m\u001b[2S")]
    [InlineData("\u001b[3;2H\u001b[44m界\u001b[41m\u001b[S")]
    [InlineData("\u001b[?69l\u001b[2;1Habcdefg界\u001b[?69h\u001b[2;6s\u001b[S")]
    [InlineData("\u001b[?69l\u001b[2;1Habcdefg界\u001b[?69h\u001b[3;8s\u001b[S")]
    public void RectangularMarginsMatchNative(string operation)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native differential available: {available}");
        if (!available) return;
        TerminalScreen managedScreen = new(8, 5, 20), nativeScreen = new(8, 5, 20);
        using BasicVtProcessor managed = new(managedScreen);
        using GhosttyVtProcessor native = new(nativeScreen);
        StringBuilder managedResponse = new(), nativeResponse = new();
        managed.ResponseCallback = bytes => managedResponse.Append(Encoding.UTF8.GetString(bytes));
        native.ResponseCallback = bytes => nativeResponse.Append(Encoding.UTF8.GetString(bytes));
        const string seed = "abcdefgh\u001b[2;1Hijklmnop\u001b[3;1Hqrstuvwx\u001b[4;1HABCDEFGH\u001b[5;1HIJKLMNOP";
        byte[] input = Encoding.UTF8.GetBytes(seed + "\u001b[?69h\u001b[3;6s\u001b[2;4r" + operation);
        managed.Process(input);
        native.Process(input);
        Assert.Equal(nativeResponse.ToString(), managedResponse.ToString());
        Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        for (int row = 0; row < 5; row++)
        {
            Assert.Equal(nativeScreen.GetViewportRow(row).WrapsToNext, managedScreen.GetViewportRow(row).WrapsToNext);
            for (int col = 0; col < 8; col++)
            {
                TerminalCell expected = nativeScreen.GetViewportRow(row).ReadOnlyCells[col];
                TerminalCell actual = managedScreen.GetViewportRow(row).ReadOnlyCells[col];
                Assert.True((expected.Codepoint == actual.Codepoint || expected.Codepoint is 0 or 32 && actual.Codepoint is 0 or 32)
                    && expected.Width == actual.Width && expected.Background == actual.Background,
                    $"Cell {row},{col}: native U+{expected.Codepoint:X}/w{expected.Width}, managed U+{actual.Codepoint:X}/w{actual.Width}");
                Assert.Equal(expected.IsWideSpacerHead, actual.IsWideSpacerHead);
                Assert.Equal(expected.Grapheme ?? "", actual.Grapheme ?? "");
            }
        }
    }

    [Fact]
    public void RectangularScrollReusesStorageAndDoesNotCreateHistory()
    {
        TerminalScreen screen = new(80, 24, 100);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[?69h\u001b[10;60s"u8);
        for (int i = 0; i < 100; i++) processor.Process("\u001b[3S\u001b[2T"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) processor.Process("\u001b[3S\u001b[2T"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(24, screen.TotalRows);
    }

    [Fact]
    public void ManagedSoftResetClearsMarginsAsPartOfExistingDecstrContract()
    {
        // Ghostty currently ignores DECSTR; RoyalTerminal deliberately retains
        // its existing VT soft-reset feature, including resetting all margins.
        using BasicVtProcessor processor = new(new TerminalScreen(8, 5));
        processor.Process("\u001b[?69h\u001b[3;6s\u001b[2;4r\u001b[!p\u001b[3;8H\rQ"u8);
        Assert.Equal((1, 2), (processor.CursorCol, processor.CursorRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyledVtRestoresMarginsOriginAndPendingWrap(bool origin)
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 5, 0));
        using BasicVtProcessor target = new(new TerminalScreen(8, 5, 0));
        source.Process(Encoding.UTF8.GetBytes("\u001b[?69h\u001b[3;6s\u001b[2;4r" +
            (origin ? "\u001b[?6h" : "\u001b[2;3H") + "ABCD"));
        TerminalSnapshotExportOptions options = new(TrimTrailingWhitespace: true,
            Extras: new(IncludeCursor: true, IncludeModes: true, IncludeScrollingRegion: true));
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        target.Process(Encoding.UTF8.GetBytes(snapshot));
        source.Process("X\u001b[6n"u8);
        target.Process("X\u001b[6n"u8);
        Assert.Equal((source.CursorCol, source.CursorRow), (target.CursorCol, target.CursorRow));
        Assert.Equal((3, 2), (target.CursorCol, target.CursorRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeResetsBothMarginAxes(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 5);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("\u001b[?69h\u001b[3;6s\u001b[2;4r\u001b[?6h"u8);
        screen.Resize(10, 6);
        processor.NotifyResize(10, 6, 100, 60);
        processor.Process("\u001b[H"u8);
        Assert.Equal((0, 0), (processor.CursorCol, processor.CursorRow));
        StringBuilder response = new();
        processor.ResponseCallback = bytes => response.Append(Encoding.UTF8.GetString(bytes));
        processor.Process("\u001bP$qs\u001b\\\u001bP$qr\u001b\\"u8);
        Assert.Contains("1;10s", response.ToString());
        Assert.Contains("1;6r", response.ToString());
    }
}
