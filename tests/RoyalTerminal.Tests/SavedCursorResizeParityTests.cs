// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SavedCursorResizeParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("abcdefghij\u001b7\u001b[H", 4, 4)]
    [InlineData("abcdefghij\u001b7\u001b[H", 12, 4)]
    [InlineData("abcdefgh\u001b7\u001b[H", 12, 4)]
    [InlineData("abcdefgh\u001b7\u001b[H", 4, 4)]
    [InlineData("ABCDEFGH\u001b[2;8H\u001b7\u001b[H", 4, 3)]
    [InlineData("abcd\u001b7\u001b[H", 4, 4)]
    [InlineData("A界BC界D\u001b7\u001b[H", 5, 4)]
    [InlineData("A界BC\u001b[1;3H\u001b7\u001b[H", 4, 4)]
    [InlineData("\u001b[4;7H\u001b7\u001b[H", 4, 6)]
    [InlineData("first\r\nsecond\r\nthird\r\nfourth\u001b[H\u001b7\u001b[4;1H", 4, 2)]
    [InlineData("first\r\nsecond\r\nthird\r\nfourth\u001b[H\u001b7\u001b[4;1H", 8, 2)]
    [InlineData("\u001b[?1049habcdefgh\u001b7\u001b[H", 12, 6)]
    [InlineData("\u001b[?1049habcdefgh\u001b7\u001b[H", 4, 2)]
    [InlineData("\u001b[?1049h\u001b[42mOLD\u001b[?1049l\u001b[?1049hNEW\u001b7", 8, 4)]
    [InlineData("\u001b[?1049hOLD\u001b[?1049hNEW\u001b7", 8, 4)]
    [InlineData("\u001b[?1049h\u001b[42mOLD\u001b[?1049l\u001bc\u001b[?1049hNEW\u001b7", 8, 4)]
    public void SavedCellTracksReflowAndPreservesTheSavedPen(string input, int columns, int rows)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native saved-cursor resize differential available: {available}");
        if (!available) return;

        TerminalScreen expected = new(8, 4), actual = new(8, 4);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        byte[] bytes = Encoding.UTF8.GetBytes("\u001b[31;44m\u001b[1\"q" + input);
        native.Process(bytes);
        managed.Process(bytes);
        expected.Resize(columns, rows, reflowOnResize: false);
        native.NotifyResize(columns, rows, columns * 8, rows * 16);
        managed.ResizeScreen(columns, rows, columns * 8, rows * 16, reflowOnResize: true);

        // Restore a different pen/charset too; reflow must only change position
        // and the saved pending-wrap flag, not discard the rest of DECSC.
        native.Process("\u001b[0m\u001b[0\"q\u001b(0\u001b8"u8);
        managed.Process("\u001b[0m\u001b[0\"q\u001b(0\u001b8"u8);
        Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        native.Process("qX"u8);
        managed.Process("qX"u8);
        Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            TerminalCell e = expected.GetViewportRow(row)[column], a = actual.GetViewportRow(row)[column];
            Assert.Equal((e.Codepoint, e.Width, e.Grapheme, e.IsProtected),
                (a.Codepoint, a.Width, a.Grapheme, a.IsProtected));
            Assert.Equal((e.ForegroundIdentity, e.BackgroundIdentity),
                (a.ForegroundIdentity, a.BackgroundIdentity));
        }
    }

    [Fact]
    public void SavedPositionIsNotTrackedDuringOutputBeforeResize()
    {
        TerminalScreen screen = new(8, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[2;3H\u001b7\u001b[4;1H\n\n\n"u8);
        processor.ResizeScreen(12, 4, 96, 64, reflowOnResize: true);
        processor.Process("\u001b8"u8);
        Assert.Equal((2, 1), (processor.CursorCol, processor.CursorRow));
    }
}
