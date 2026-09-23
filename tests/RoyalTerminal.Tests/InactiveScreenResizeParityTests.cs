// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class InactiveScreenResizeParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("abcdefghijklmno", 4, 4)]
    [InlineData("abcdefghijklmno", 12, 4)]
    [InlineData("abcdefghijklmno", 4, 6)]
    [InlineData("abcdefghijklmno", 12, 2)]
    [InlineData("first\r\nsecond\r\nthird\r\nfourth\r\nfifth", 5, 4)]
    [InlineData("A界BC界D", 5, 4)]
    [InlineData("\u001b]133;A;redraw=1\aprompt\u001b]133;B\ainput", 5, 4)]
    [InlineData("\u001b]133;A;redraw=last\aprompt\u001b]133;B\ainput", 12, 4)]
    [InlineData("abcdefghijklmno\u001b[?7l", 4, 4)]
    public void BothBuffersAndSavedCursorsMatchAfterResizingWhileAlternateIsActive(string primary, int columns, int rows)
    {
        if (!Available()) return;
        TerminalScreen expected = new(8, 4), actual = new(8, 4);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        Write("\u001b[31m" + primary + "\u001b[?1049h\u001b[H\u001b[32mALT\u001b7");
        Resize(columns, rows);
        Compare(expected, native, actual, managed);
        Write("\u001b[?1049l");
        Compare(expected, native, actual, managed);
        Write("X");
        Compare(expected, native, actual, managed);
        Write("\u001b[?47h\u001b8Y");
        Compare(expected, native, actual, managed);

        void Write(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            native.Process(bytes); managed.Process(bytes);
        }
        void Resize(int width, int height)
        {
            expected.Resize(width, height, reflowOnResize: false);
            native.NotifyResize(width, height, width * 8, height * 16);
            managed.ResizeScreen(width, height, width * 8, height * 16, reflowOnResize: true);
        }
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(12, 6)]
    [InlineData(8, 2)]
    public void DormantAlternateDoesNotReflowAndItsSavedCursorTracksEachResize(int columns, int rows)
    {
        if (!Available()) return;
        TerminalScreen expected = new(8, 4), actual = new(8, 4);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        byte[] initial = Encoding.UTF8.GetBytes("PRIMARY\u001b[?47h\u001b[HALT-LONG\r\nSECOND\u001b7\u001b[?47l");
        native.Process(initial); managed.Process(initial);
        foreach ((int width, int height) in new[] { (columns, rows), (8, 4) })
        {
            expected.Resize(width, height, reflowOnResize: false);
            native.NotifyResize(width, height, width * 8, height * 16);
            managed.ResizeScreen(width, height, width * 8, height * 16, reflowOnResize: true);
            Compare(expected, native, actual, managed);
        }
        native.Process("\u001b[?47h\u001b8X"u8);
        managed.Process("\u001b[?47h\u001b8X"u8);
        Compare(expected, native, actual, managed);
    }

    [Fact]
    public void InactiveResizeScopeRestoresTheVisibleBufferOnFailure()
    {
        TerminalScreen screen = new(8, 4);
        screen.GetViewportRow(0)[0].Codepoint = 'P';
        screen.SwitchToAlternateBuffer(clear: false);
        screen.GetViewportRow(0)[0].Codepoint = 'A';
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using TerminalScreen.InactiveResizeScope scope = screen.EnterInactiveResize(8, 4);
            Assert.True(scope.Available);
            Assert.False(screen.AlternateBufferActive);
            screen.Resize(0, 4);
        });
        Assert.True(screen.AlternateBufferActive);
        Assert.Equal((8, 4), (screen.Columns, screen.ViewportRows));
        Assert.Equal('A', screen.GetViewportRow(0)[0].Codepoint);
        screen.SwitchToPrimaryBuffer();
        Assert.Equal('P', screen.GetViewportRow(0)[0].Codepoint);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native inactive resize differential available: {available}");
        return available;
    }

    private static void Compare(TerminalScreen expected, IVtProcessor native, TerminalScreen actual, IVtProcessor managed)
    {
        Assert.Equal(native.AlternateScreen, managed.AlternateScreen);
        Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        for (int row = 0; row < actual.ViewportRows; row++)
        {
            TerminalRow e = expected.GetViewportRow(row), a = actual.GetViewportRow(row);
            Assert.Equal((e.WrapsToNext, e.IsWrapContinuation, e.SemanticPrompt), (a.WrapsToNext, a.IsWrapContinuation, a.SemanticPrompt));
            for (int column = 0; column < actual.Columns; column++)
            {
                TerminalCell ec = e[column], ac = a[column];
                Assert.True((ec.Codepoint, ec.Width, ec.Grapheme) == (ac.Codepoint, ac.Width, ac.Grapheme),
                    $"Cell {column},{row}: expected {ec.Codepoint}/{ec.Width}, actual {ac.Codepoint}/{ac.Width}");
                Assert.Equal((ec.ForegroundIdentity, ec.BackgroundIdentity, ec.SemanticContent),
                    (ac.ForegroundIdentity, ac.BackgroundIdentity, ac.SemanticContent));
            }
        }
    }
}
