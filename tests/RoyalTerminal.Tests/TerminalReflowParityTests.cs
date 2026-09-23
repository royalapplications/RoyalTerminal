// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalReflowParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("abcdef", 3)]
    [InlineData("abcdef", 4)]
    [InlineData("abcdef\u001b[1;8H", 4)]
    [InlineData("abc     ", 4)]
    [InlineData("abc\u001b[44m     \u001b[0m", 4)]
    [InlineData("abc\u001b[44m\u001b[K\u001b[0m", 4)]
    [InlineData("abc\r\n\r\nlast", 4)]
    [InlineData("12345678", 4)]
    [InlineData("123456789", 16)]
    [InlineData("界界界", 4)]
    public void BlankCursorAndTrailingRowsMatchNativeViewport(string input, int columns)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native reflow differential available: {available}");
        if (!available) return;
        TerminalScreen expected = new(8, 8), actual = new(8, 8);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        native.Process(bytes);
        managed.Process(bytes);
        expected.Resize(columns, 8, reflowOnResize: false);
        native.NotifyResize(columns, 8, columns * 8, 128);
        managed.ResizeScreen(columns, 8, columns * 8, 128, reflowOnResize: true);
        Assert.Equal(native.CursorCol, managed.CursorCol);
        Assert.Equal(native.CursorRow, managed.CursorRow);
        for (int row = 0; row < 8; row++)
        {
            TerminalRow e = expected.GetViewportRow(row), a = actual.GetViewportRow(row);
            Assert.Equal(e.WrapsToNext, a.WrapsToNext);
            Assert.Equal(e.IsWrapContinuation, a.IsWrapContinuation);
            for (int column = 0; column < columns; column++)
            {
                TerminalCell ec = e[column], ac = a[column];
                Assert.Equal((ec.Codepoint, ec.Width, ec.BackgroundIdentity),
                    (ac.Codepoint, ac.Width, ac.BackgroundIdentity));
            }
        }
    }

    [Fact]
    public void TrailingBlankCellAnchorPreventsPruningItsRow()
    {
        TerminalScreen screen = new(8, 8);
        using BasicVtProcessor processor = new(screen);
        processor.Process("text"u8);
        TerminalScreenAnchor anchor = screen.CreateAnchor(7, 5);
        processor.ResizeScreen(4, 8, 32, 128, reflowOnResize: true);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.InRange(position.Row, 0, screen.TotalRows - 1);
        screen.ReleaseAnchor(anchor);
    }
}
