// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalActiveRowTests
{
    public static TheoryData<bool, bool, string, string> Cases
    {
        get
        {
            TheoryData<bool, bool, string, string> data = new();
            foreach (bool native in new[] { false, true })
                foreach (bool held in new[] { false, true })
                {
                    data.Add(native, held, "X", "dXdd");
                    data.Add(native, held, "界", "d界d");
                    data.Add(native, held, "\u001b[P", "ddd");
                    data.Add(native, held, "\u001b[@", "d dd");
                    data.Add(native, held, "\u001b[K", "d");
                }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ScrolledViewportNeverRedirectsCursorWrites(bool native, bool held, string command, string lastRow)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native library unavailable.");
        // Ghostty uses active-screen coordinates; xterm.js adds cursor.y to
        // ybase, not ydisp. WT's cursor belongs to its active text buffer too.
        TerminalScreen screen = new(4, 2, 100);
        FrozenClock clock = new();
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen, clock)
            : new BasicVtProcessor(screen, new() { TimeProvider = clock });
        // The native host projects only captured rows; include both offscreen
        // active rows before asserting their held presentation below.
        if (processor is GhosttyVtProcessor ghostty) ghostty.RenderOverscan = new(0, 2);
        processor.Process("aaaa\r\nbbbb\r\ncccc\r\ndddd"u8);
        if (processor is ITerminalViewportScrollSource scroll) scroll.SetViewportOffsetRows(0);
        else screen.ScrollOffset = 2;
        Assert.Equal(0, screen.ViewportTopAbsoluteRow);
        if (held) processor.Process("\u001b[?2026h"u8);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[2;2H" + command));
        Assert.Equal('a', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('b', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        if (held)
        {
            TerminalRenderViewport captured = screen.GetRenderViewport(new(0, 2));
            Assert.Equal(4, captured.Count);
            Assert.Equal('d', captured[3].Row.ReadOnlyCells[1].Codepoint);
            processor.Process("\u001b[?2026l"u8);
        }
        Assert.Equal(0, screen.ViewportTopAbsoluteRow);
        Assert.Equal("aaaa\nbbbb\ncccc\n" + lastRow,
            ((ITerminalBufferSelectionExportSource)processor).ReadBufferSelection(new(0, 0, 3, 3), unwrap: true));
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }
}
