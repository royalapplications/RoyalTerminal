// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Managed selection export and paste encoding parity coverage.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalSelectionAndPasteTests
{
    [Fact]
    public void BasicVtProcessor_SelectionExport_FormatsViewportSelection()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABC\r\nDEF"u8);

        string? selection = ((ITerminalSelectionExportSource)processor)
            .ReadSelection(new TerminalSelectionRange(1, 0, 2, 0));

        Assert.Equal("BC", selection);
    }

    [Fact]
    public void BasicVtProcessor_PasteEncoding_MatchesGhosttyRules()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using BasicVtProcessor processor = new(screen);

        ITerminalPasteSequenceEncoderSource pasteEncoder = processor;
        Assert.False(pasteEncoder.IsPasteSafe("hello\nworld"));
        Assert.True(pasteEncoder.TryEncodePaste("hel\x1blo\x00world", bracketedPaste: true, out byte[] bracketed));
        Assert.Equal("\x1b[200~hel lo world\x1b[201~", Encoding.UTF8.GetString(bracketed));

        Assert.True(pasteEncoder.TryEncodePaste("hello\nworld", bracketedPaste: false, out byte[] unbracketed));
        Assert.Equal("hello\rworld", Encoding.UTF8.GetString(unbracketed));
    }

    [Fact]
    public void BasicVtProcessor_SnapshotExport_SupportsPlainTextVtAndHtml()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABC\r\nDEF\r\n"u8);

        ITerminalSnapshotExportSource exporter = processor;
        Assert.True(exporter.SupportsSnapshotFormat(TerminalSnapshotExportFormat.PlainText));
        Assert.True(exporter.SupportsSnapshotFormat(TerminalSnapshotExportFormat.StyledVt));
        Assert.True(exporter.SupportsSnapshotFormat(TerminalSnapshotExportFormat.Html));
        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, new TerminalSnapshotExportOptions(), out string snapshot));
        Assert.Contains("ABC", snapshot);
        Assert.Contains("DEF", snapshot);
    }

    [Fact]
    public void BasicVtProcessor_SnapshotExport_UnwrapsSoftWrappedRows()
    {
        TerminalScreen screen = new(columns: 4, viewportRows: 4, scrollbackLimit: 8);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABCDE\r\nZ"u8);

        ITerminalSnapshotExportSource exporter = processor;
        Assert.True(
            exporter.TryExportSnapshot(
                TerminalSnapshotExportFormat.PlainText,
                new TerminalSnapshotExportOptions(Unwrap: true, TrimTrailingWhitespace: true),
                out string snapshot));

        Assert.Equal("ABCDE" + Environment.NewLine + "Z", snapshot);
    }

    [Fact]
    public void BasicVtProcessor_SnapshotExport_StyledVt_FormatsHyperlinksAndCursor()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 8);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b]8;;https://example.com\u001b\\AB\u001b]8;;\u001b\\"u8);

        ITerminalSnapshotExportSource exporter = processor;
        TerminalSnapshotExportOptions options = new(
            Extras: new TerminalSnapshotExportExtras(
                IncludeCursor: true,
                IncludeStyle: true,
                IncludeHyperlinks: true));

        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        Assert.Contains("\u001b]8;;https://example.com\u001b\\", snapshot, StringComparison.Ordinal);
        Assert.Contains("AB", snapshot, StringComparison.Ordinal);
        Assert.Contains("\u001b[1;3H", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void BasicVtProcessor_SnapshotExport_Html_FormatsStyledLinks()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 8);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1m\u001b]8;;https://example.com\u001b\\AB\u001b]8;;\u001b\\\u001b[0m"u8);

        ITerminalSnapshotExportSource exporter = processor;
        TerminalSnapshotExportOptions options = new(
            Extras: new TerminalSnapshotExportExtras(IncludeHyperlinks: true));

        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.Html, options, out string snapshot));
        Assert.Contains("<a href=\"https://example.com\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("font-weight:bold", snapshot, StringComparison.Ordinal);
        Assert.Contains('A', snapshot);
        Assert.Contains('B', snapshot);
    }

    [Fact]
    public void BasicVtProcessor_RectangularSelectionExport_UsesColumnBand()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABCD\r\nEFGH"u8);

        string? selection = ((ITerminalSelectionExportSource)processor)
            .ReadSelection(new TerminalSelectionRange(1, 0, 2, 1, Rectangle: true));

        Assert.Equal("BC" + Environment.NewLine + "FG", selection);
    }

    [Fact]
    public void BasicVtProcessor_SnapshotExport_RectangularSelection_DoesNotUnwrapAcrossRows()
    {
        TerminalScreen screen = new(columns: 4, viewportRows: 4, scrollbackLimit: 8);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABCDE"u8);

        ITerminalSnapshotExportSource exporter = processor;
        TerminalSnapshotExportOptions options = new(
            Unwrap: true,
            TrimTrailingWhitespace: true,
            Selection: new TerminalSelectionRange(0, 0, 0, 1, Rectangle: true));

        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string snapshot));
        Assert.Equal("A" + Environment.NewLine + "E", snapshot);
    }

    [Fact]
    public void BasicVtProcessor_SnapshotExport_StyledVt_ExportsTrackedModes()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 8);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[?25l\u001b[?7l"u8);

        ITerminalSnapshotExportSource exporter = processor;
        TerminalSnapshotExportOptions options = new(
            Extras: new TerminalSnapshotExportExtras(IncludeModes: true));

        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        Assert.Contains("\u001b[?25l", snapshot, StringComparison.Ordinal);
        Assert.Contains("\u001b[?7l", snapshot, StringComparison.Ordinal);
    }
}
