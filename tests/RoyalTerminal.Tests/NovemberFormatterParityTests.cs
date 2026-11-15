// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty formatter.zig 9781c0afc: a delayed wrap can be at the active
// right margin or at the physical edge outside that margin.
public sealed class NovemberFormatterParityTests
{
    [Theory]
    [InlineData(false, "\u001b[?69h\u001b[;2s00")]
    [InlineData(true, "\u001b[?69h\u001b[;2s00")]
    [InlineData(false, "\u001b[?69h\u001b[3;6s\u001b[1;5H😀")]
    [InlineData(true, "\u001b[?69h\u001b[3;6s\u001b[1;5H😀")]
    [InlineData(false, "\u001b[?69h\u001b[3;6s\u001b[1;20Hx")]
    [InlineData(true, "\u001b[?69h\u001b[3;6s\u001b[1;20Hx")]
    public void MarginWrapSurvivesStyledExportAndContinuation(bool native, string input)
    {
        Assert.True(GhosttyVtProcessor.IsAvailable(), "Native differential is required.");
        TerminalScreen expected = new(20, 4);
        using IVtProcessor source = native ? new GhosttyVtProcessor(expected) : new BasicVtProcessor(expected);
        source.Process(Encoding.UTF8.GetBytes(input));
        TerminalSnapshotExportOptions options = new(Extras: new(IncludeCursor: true, IncludeStyle: true,
            IncludeHyperlinks: true, IncludeProtection: true, IncludeCharsets: true, IncludeModes: true, IncludeScrollingRegion: true));
        Assert.True(((ITerminalSnapshotExportSource)source).TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string text));
        (int Column, int Row) cursor = (source.CursorCol, source.CursorRow);
        source.Process("X"u8);
        foreach (bool replayNative in new[] { false, true })
        {
            TerminalScreen actual = new(20, 4);
            using IVtProcessor replay = replayNative ? new GhosttyVtProcessor(actual) : new BasicVtProcessor(actual);
            replay.Process(Encoding.UTF8.GetBytes(text));
            Assert.Equal(cursor, (replay.CursorCol, replay.CursorRow));
            replay.Process("X"u8);
            Assert.Equal((source.CursorCol, source.CursorRow), (replay.CursorCol, replay.CursorRow));
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 20; column++)
            {
                TerminalCell left = expected.GetViewportRow(row).ReadOnlyCells[column];
                TerminalCell right = actual.GetViewportRow(row).ReadOnlyCells[column];
                Assert.Equal((left.Codepoint, left.Width, left.Grapheme), (right.Codepoint, right.Width, right.Grapheme));
            }
        }
    }
}
