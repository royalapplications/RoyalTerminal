// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSynchronizedOutputTests
{
    [Fact]
    public void HoldPublishesCompletedPrefixButKeepsParsingQueriesAndCursorState()
    {
        TerminalScreen screen = new(8, 2, 16);
        using BasicVtProcessor processor = new(screen);
        string? reply = null;
        processor.ResponseCallback = bytes => reply = Encoding.ASCII.GetString(bytes);

        processor.Process("A\u001b[?2026hB\u001b[?25l\u001b[6n"u8);
        Assert.Equal("A", Text(screen.GetViewportRow(0)));
        Assert.Equal(1, processor.CursorCol);
        Assert.True(processor.CursorVisible);
        Assert.Equal("\u001b[1;3R", reply);
        Assert.NotNull(processor.NextTimedRefreshDelay);

        processor.Process("\u001b[?2026l"u8);
        Assert.Equal("AB", Text(screen.GetViewportRow(0)));
        Assert.Equal(2, processor.CursorCol);
        Assert.False(processor.CursorVisible);
        Assert.Null(processor.NextTimedRefreshDelay);
    }

    [Fact]
    public void TimeoutPublishesWithoutNewInputAndRepeatedEnableDoesNotExtendDeadline()
    {
        ManualClock clock = new();
        TerminalScreen screen = new(8, 2, 16);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        processor.Process("A\u001b[?2026hB"u8);
        clock.Advance(TimeSpan.FromMilliseconds(900));
        processor.Process("\u001b[?2026hC"u8);
        Assert.Equal(TimeSpan.FromMilliseconds(100), processor.NextTimedRefreshDelay);
        Assert.False(processor.RefreshTimedState());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(TimeSpan.Zero, processor.NextTimedRefreshDelay);
        Assert.True(processor.RefreshTimedState());
        Assert.False(processor.RefreshTimedState());
        Assert.Equal("ABC", Text(screen.GetViewportRow(0)));

        string? reply = null;
        processor.ResponseCallback = bytes => reply = Encoding.ASCII.GetString(bytes);
        processor.Process("\u001b[?2026$p"u8);
        Assert.Equal("\u001b[?2026;2$y", reply);
    }

    [Fact]
    public void HoldTransfersAlternateAndPrimaryHistoryWithoutLosingHyperlinksOrStyles()
    {
        TerminalScreen screen = new(8, 2, 16);
        using BasicVtProcessor processor = new(screen);
        processor.Process("history\r\nprimary\u001b[?2026h\u001b[?1049h\u001b]8;;https://example.com\u001b\\\u001b[31mALT"u8);
        Assert.False(screen.AlternateBufferActive);
        Assert.False(processor.AlternateScreen);
        Assert.Equal("primary", Text(screen.GetViewportRow(1)));

        processor.Process("\u001b[?2026l"u8);
        Assert.True(screen.AlternateBufferActive);
        Assert.True(processor.AlternateScreen);
        Assert.Equal("ALT", Text(screen.GetViewportRow(0)));
        int hyperlink = screen.GetViewportRow(0)[0].HyperlinkId;
        Assert.True(screen.TryGetHyperlinkUrl(hyperlink, out string? url));
        Assert.Equal("https://example.com", url);
        Assert.NotEqual(screen.DefaultForeground, screen.GetViewportRow(0)[0].Foreground);

        processor.Process("\u001b[?1049l"u8);
        Assert.Equal("history", Text(screen.GetViewportRow(0)));
        Assert.Equal("primary", Text(screen.GetViewportRow(1)));
    }

    [Fact]
    public void ResetAndResizeEndTheHold()
    {
        TerminalScreen screen = new(8, 2, 16);
        using BasicVtProcessor processor = new(screen);
        processor.Process("A\u001b[?2026hB"u8);
        processor.ResizeScreen(10, 3, 100, 30, reflowOnResize: true);
        Assert.Null(processor.NextTimedRefreshDelay);
        Assert.Equal(10, screen.Columns);
        Assert.Equal(3, screen.ViewportRows);
        Assert.Equal("AB", Text(screen.GetViewportRow(0)));

        processor.Process("\u001b[?2026hC\u001bc"u8);
        Assert.Null(processor.NextTimedRefreshDelay);
        Assert.Equal(string.Empty, Text(screen.GetViewportRow(0)));
        Assert.Equal(0, processor.CursorCol);
    }

    [Fact]
    public void HoldTransfersRasterStateAndSupportsAnotherFrame()
    {
        TerminalScreen screen = new(8, 2, 16);
        using BasicVtProcessor processor = new(screen, new() { SixelGraphicsEnabled = true });
        processor.NotifyResize(8, 2, 80, 20);
        processor.Process("\u001b[?2026h\u001bPq\"1;1;1;6#1;2;100;0;0#1@\u001b\\"u8);
        Assert.False(screen.HasRasterGraphics);
        processor.Process("\u001b[?2026l"u8);
        Assert.True(screen.HasRasterGraphics);

        processor.Process("\u001b[?2026h\u001b[2J"u8);
        Assert.True(screen.HasRasterGraphics);
        processor.Process("\u001b[?2026l"u8);
        Assert.False(screen.HasRasterGraphics);
    }

    private static string Text(TerminalRow row)
    {
        StringBuilder text = new();
        foreach (TerminalCell cell in row.ReadOnlyCells)
        {
            if (cell.Codepoint != 0) text.Append(char.ConvertFromUtf32(cell.Codepoint));
        }
        return text.ToString();
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
