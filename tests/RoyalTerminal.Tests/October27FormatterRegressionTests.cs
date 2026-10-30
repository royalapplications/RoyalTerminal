// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #14711/#14725/#14766/#14670. These assert replayed cell semantics,
// not a particular spelling of equivalent SGR/OSC 8 sequences.
public sealed class October27FormatterRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErasedBackgroundRowsReplayAsEmptyColoredCells(bool native)
    {
        using IVtProcessor source = Create(native, 8, 4, out _);
        source.Process("\u001b[44m\u001b[2J"u8);
        string snapshot = Export(source, new());
        Assert.Contains("X", snapshot);
        foreach (bool replayNative in new[] { false, true })
        {
            using IVtProcessor replay = Create(replayNative, 8, 4, out TerminalScreen screen);
            replay.Process(Encoding.UTF8.GetBytes(snapshot));
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 8; column++)
            {
                TerminalCell cell = screen.GetViewportRow(row).ReadOnlyCells[column];
                Assert.Equal(0, cell.Codepoint);
                Assert.Equal(TerminalColorIdentity.Palette(4), cell.BackgroundIdentity);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlankGapsStayEmptyAndUnlinkedWhileCellHyperlinksAreAlwaysExported(bool native)
    {
        using IVtProcessor source = Create(native, 8, 4, out _);
        source.Process("\u001b[1m\u001b]8;id=stable;https://example.com\u001b\\A\u001b[4GB\u001b]8;;\u001b\\"u8);
        string snapshot = Export(source, new(TrimTrailingWhitespace: true));
        Assert.Contains("https://example.com", snapshot);
        foreach (bool replayNative in new[] { false, true })
        {
            using IVtProcessor replay = Create(replayNative, 8, 4, out TerminalScreen screen);
            replay.Process(Encoding.UTF8.GetBytes(snapshot));
            for (int column = 1; column < 3; column++)
            {
                TerminalCell blank = screen.GetViewportRow(0).ReadOnlyCells[column];
                Assert.Equal(0, blank.Codepoint);
                Assert.Equal(CellAttributes.None, blank.Attributes);
                Assert.Equal(0, blank.HyperlinkId);
            }
            foreach (int column in new[] { 0, 3 })
            {
                TerminalCell cell = screen.GetViewportRow(0).ReadOnlyCells[column];
                Assert.True(screen.TryGetHyperlink(cell.HyperlinkId, out TerminalHyperlink? link));
                Assert.NotNull(link);
                Assert.Equal("https://example.com", link.Uri);
                Assert.Equal("stable", Encoding.UTF8.GetString(link.ExplicitId));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnwrappedBlankCellsDoNotInheritThePrecedingStyleOrHyperlink(bool native)
    {
        using IVtProcessor source = Create(native, 10, 3, out _);
        source.Process(Encoding.UTF8.GetBytes("\u001b[1m\u001b]8;;https://example.com\u001b\\abc\u001b[10G中\u001b]8;;\u001b\\"));
        string snapshot = Export(source, new(Unwrap: true, TrimTrailingWhitespace: true));
        using IVtProcessor replay = Create(false, 20, 3, out TerminalScreen screen);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        for (int column = 3; column < 9; column++)
        {
            TerminalCell blank = screen.GetViewportRow(0).ReadOnlyCells[column];
            Assert.Equal(' ', blank.Codepoint);
            Assert.Equal(CellAttributes.None, blank.Attributes);
            Assert.Equal(0, blank.HyperlinkId);
        }
        Assert.Equal('中', screen.GetViewportRow(0).ReadOnlyCells[9].Codepoint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TrailingBlankRowsFollowTheTrimOption(bool native, bool trim)
    {
        using IVtProcessor source = Create(native, 8, 4, out _);
        string snapshot = Export(source, new(TrimTrailingWhitespace: trim));
        using IVtProcessor replay = Create(false, 8, 4, out _);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        Assert.Equal(trim ? 0 : 3, replay.CursorRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlankRowsAfterHistoryPreserveViewportGeometry(bool native)
    {
        using IVtProcessor source = Create(native, 8, 5, out _);
        source.Process("1\r\n2\r\n3\r\n4\r\n5\r\n6\r\n7\r\n8\u001b[3;1H\u001b[J"u8);
        string snapshot = Export(source, new(Extras: new(IncludeCursor: true)));
        using IVtProcessor replay = Create(false, 8, 5, out TerminalScreen screen);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        Assert.Equal((2, 0), (replay.CursorRow, replay.CursorCol));
        Assert.Equal('4', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('5', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        Assert.Equal(0, screen.GetViewportRow(2).ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingWrapOverErasedCellDoesNotCreateText(bool native)
    {
        using IVtProcessor source = Create(native, 4, 2, out _);
        source.Process("abcd\u001b7\u001b[2K\u001b8"u8);
        string snapshot = Export(source, new(Extras: new(IncludeCursor: true)));
        foreach (bool replayNative in new[] { false, true })
        {
            using IVtProcessor replay = Create(replayNative, 4, 2, out TerminalScreen screen);
            replay.Process(Encoding.UTF8.GetBytes(snapshot));
            Assert.Equal(0, screen.GetViewportRow(0).ReadOnlyCells[3].Codepoint);
            Assert.Equal((0, 3), (replay.CursorRow, replay.CursorCol));
            replay.Process("X"u8);
            Assert.Equal('X', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        }
    }

    private static string Export(IVtProcessor processor, TerminalSnapshotExportOptions options)
    {
        Assert.True(((ITerminalSnapshotExportSource)processor).TryExportSnapshot(
            TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        return snapshot;
    }

    private static IVtProcessor Create(bool native, int columns, int rows, out TerminalScreen screen)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        screen = new(columns, rows, 100);
        return native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    }
}
