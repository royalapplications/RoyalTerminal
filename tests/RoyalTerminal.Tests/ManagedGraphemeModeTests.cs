// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedGraphemeModeTests
{
    [Theory]
    [InlineData(false, "\U0001F1E6\U0001F1E7", 4)]
    [InlineData(true, "\U0001F1E6\U0001F1E7", 2)]
    [InlineData(false, "#\uFE0F\u20E3", 1)]
    [InlineData(true, "#\uFE0F\u20E3", 2)]
    [InlineData(false, "☔\uFE0E", 2)]
    [InlineData(true, "☔\uFE0E", 1)]
    [InlineData(false, "\u0301A", 1)]
    [InlineData(true, "\u0301A", 1)]
    [InlineData(false, "\u0600A", 2)]
    [InlineData(true, "\u0600A", 2)]
    public void ModeChangesPrintingAndReportsItsActualState(bool clusters, string text, int cursor)
    {
        TerminalScreen screen = new(12, 3, 0);
        using BasicVtProcessor processor = new(screen);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.Process(Encoding.UTF8.GetBytes($"\u001b[?2027{(clusters ? 'h' : 'l')}{text}\u001b[?2027$p"));
        Assert.Equal(cursor, processor.CursorCol);
        Assert.Equal($"\u001b[?2027;{(clusters ? 1 : 2)}$y", Assert.Single(replies));
    }

    [Theory]
    [InlineData("\u001bc")]
    [InlineData("\u001b[!p")]
    public void ResetsRestoreNonClusterMode(string reset)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(12, 3, 0));
        string? reply = null;
        processor.ResponseCallback = bytes => reply = Encoding.ASCII.GetString(bytes);
        processor.Process(Encoding.ASCII.GetBytes("\u001b[?2027h" + reset + "\u001b[?2027$p"));
        Assert.Equal("\u001b[?2027;2$y", reply);
    }

    [Theory]
    [InlineData("\U0001F1E6\U0001F1E7")]
    [InlineData("\U0001F469\u200D\u2764\uFE0F\u200D\U0001F469")]
    [InlineData("A\U0001F3FB")]
    [InlineData("#\uFE0F\u20E3")]
    [InlineData("☔\uFE0E")]
    [InlineData("\u0301A")]
    [InlineData("\u0600A")]
    [InlineData("?\u094D\u0924")]
    [InlineData("A\r\n\u0301B")]
    [InlineData("\u001b[?7lABCDEFGHIJKL\u0301")]
    [InlineData("ABCDEFGHIJK#\uFE0F\u20E3")]
    [InlineData("☔\uFE0E\u001b[?2027h\u0301")]
    [InlineData("A\u1161\u001b[?2027h\u0301")]
    public void ManagedStreamingMatchesNativeWithModeOffAndOn(string text)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        foreach (bool clusters in new[] { false, true })
        {
            TerminalScreen managedScreen = new(12, 3, 20);
            TerminalScreen nativeScreen = new(12, 3, 20);
            using BasicVtProcessor managed = new(managedScreen);
            using GhosttyVtProcessor native = new(nativeScreen);
            byte[] sequence = Encoding.UTF8.GetBytes($"\u001b[?2027{(clusters ? 'h' : 'l')}{text}");
            foreach (byte value in sequence)
            {
                managed.Process([value]);
                native.Process([value]);
            }
            Assert.Equal(native.CursorCol, managed.CursorCol);
            Assert.Equal(native.CursorRow, managed.CursorRow);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 12; column++)
            {
                TerminalCell expected = nativeScreen.GetViewportRow(row)[column];
                TerminalCell actual = managedScreen.GetViewportRow(row)[column];
                Assert.True(expected.Codepoint == actual.Codepoint && expected.Grapheme == actual.Grapheme && expected.Width == actual.Width,
                    $"mode={clusters}, row={row}, col={column}: native={expected.Codepoint:X}/{expected.Grapheme}/{expected.Width}, managed={actual.Codepoint:X}/{actual.Grapheme}/{actual.Width}");
            }
        }
    }
}
