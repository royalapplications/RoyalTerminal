// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyZeroHistoryCoordinateTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void HiddenPageRowsDoNotShiftLinksSelectionSearchOrSnapshots(bool alternate, int rows)
    {
        Assert.SkipWhen(!GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        TerminalScreen screen = new(160, rows, alternate ? 100 : 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.RenderOverscan = new(2, 2);
        if (alternate) processor.Process("\x1b[?1049h"u8);
        for (int line = 0; line < 600; line++) processor.Process("old\r\n"u8);
        processor.Process(Encoding.UTF8.GetBytes($"\x1b[H\x1b[2J\x1b[{rows};1H\x1b]8;id=stable;https://example.com/live\x1b\\alpha beta\x1b]8;;\x1b\\"));

        Assert.Equal(new TerminalViewportScrollState((ulong)rows, 0, (ulong)rows), processor.ViewportScrollState);
        Assert.Equal(default, processor.PublishedViewportPosition);
        Assert.Equal(default, screen.GetRenderViewport(new(2, 2)).CapturedOverscan);
        AssertLink(screen, rows - 1);
        Assert.Equal("alpha", processor.ReadSelection(new(0, rows - 1, 4, rows - 1)));
        Assert.Equal("alpha", processor.ReadBufferSelection(new(0, rows - 1, 4, rows - 1), unwrap: true));
        Assert.True(processor.TryGetWordExtent(new(2, rows - 1), [], out TerminalWordExtent word));
        Assert.Equal(new TerminalGridPosition(0, rows - 1), word.Start);
        Assert.Equal(new TerminalGridPosition(5, rows - 1), word.End);
        Assert.True(processor.TryGetLineExtent(new(2, rows - 1), [], false, out TerminalLineExtent lineExtent));
        Assert.Equal(new TerminalGridPosition(0, rows - 1), lineExtent.Start);
        Assert.Equal(new TerminalGridPosition(10, rows - 1), lineExtent.End);

        List<TerminalSearchMatch> matches = [];
        processor.PopulateSearchMatches("alpha", matches);
        Assert.Equal(new TerminalSearchMatch(rows - 1, 0, 4), Assert.Single(matches));
        processor.PopulateSearchMatches("old", matches);
        Assert.Empty(matches);
        Assert.True(processor.TryCreateScreenSnapshot(0, rows, 0, out TerminalScreen snapshot));
        AssertLink(snapshot, rows - 1);
        Assert.Equal('a', snapshot.GetViewportRow(rows - 1).ReadOnlyCells[0].Codepoint);

        processor.ScrollViewportToTop();
        processor.ScrollViewportToBottom();
        Assert.Equal(0UL, processor.ViewportScrollState.MaxOffsetRows);
        AssertLink(screen, rows - 1);
    }

    private static void AssertLink(TerminalScreen screen, int row)
    {
        Assert.True(screen.TryGetHyperlink(screen.GetViewportRow(row).ReadOnlyCells[0].HyperlinkId, out TerminalHyperlink? link));
        Assert.Equal("https://example.com/live", link!.Uri);
        Assert.Equal("stable", Encoding.UTF8.GetString(link.ExplicitId));
    }
}
