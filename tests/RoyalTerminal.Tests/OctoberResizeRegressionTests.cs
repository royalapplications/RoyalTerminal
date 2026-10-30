// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class OctoberResizeRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedCursorSurvivesRepeatedWidening(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 5);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        // Upstream Ghostty #14478: saved pin on a deferred hard break.
        processor.Process("abc\r\nAAA|\u001b7"u8);
        Resize(processor, screen, 5, 5);
        Resize(processor, screen, 6, 5);
        processor.Process("\u001b8X"u8);
        Assert.Equal((5, 1), (processor.CursorCol, processor.CursorRow));
        Assert.Equal('X', screen.GetViewportRow(1)[4].Codepoint);
        Assert.Equal('|', screen.GetViewportRow(1)[3].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSizeResizeKeepsGridAndMarginsButUpdatesPixelReports(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 6);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("\u001b[2;5r\u001b[?69h\u001b[2;7s\u001b[?6h"u8);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.NotifyResize(8, 6, 160, 120);
        processor.Process("\u001b[14t\u001b[16t"u8);
        Assert.Equal(new[] { "\u001b[4;120;160t", "\u001b[6;20;20t" }, replies);
        processor.Process("\u001b[H"u8);
        Assert.Equal((1, 1), (processor.CursorCol, processor.CursorRow));
        processor.Process("\u001b[99;99H"u8);
        Assert.Equal((6, 4), (processor.CursorCol, processor.CursorRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternateScreenShrinkClearsBothHalvesOfCutWideCharacter(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 4);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[?1049hABC界Z"));
        Resize(processor, screen, 4, 4);
        Assert.True(screen.GetViewportRow(0)[3].Codepoint is 0 or ' ');
        Assert.Equal(1, screen.GetViewportRow(0)[3].Width);
        processor.Process("\u001b[H\u001b[2JX"u8);
        Assert.Equal('X', screen.GetViewportRow(0)[0].Codepoint);
    }

    private static void Resize(IVtProcessor processor, TerminalScreen screen, int columns, int rows)
    {
        if (processor is BasicVtProcessor managed)
            managed.ResizeScreen(columns, rows, columns * 8, rows * 16, reflowOnResize: true);
        else
        {
            screen.Resize(columns, rows, reflowOnResize: false);
            ((GhosttyVtProcessor)processor).NotifyResize(columns, rows, columns * 8, rows * 16);
        }
    }
}
