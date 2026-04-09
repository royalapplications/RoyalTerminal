// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Native Ghostty VT processor integration coverage.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public class GhosttyVtProcessorTests
{
    [Fact]
    public void GhosttyVtProcessor_ViewportScrollState_TracksNativeScrollback_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.NotifyResize(columns: 8, rows: 4, widthPx: 64, heightPx: 64);

        StringBuilder builder = new();
        for (int i = 0; i < 10; i++)
        {
            builder.Append("L");
            builder.Append(i.ToString("D2"));
            builder.Append("\r\n");
        }

        processor.Process(Encoding.UTF8.GetBytes(builder.ToString()));

        TerminalViewportScrollState bottom = processor.ViewportScrollState;
        Assert.True(bottom.TotalRows > bottom.VisibleRows);
        Assert.Equal(bottom.MaxOffsetRows, bottom.OffsetRows);

        string bottomRowText = ReadAsciiPrefix(screen, row: 0, columns: 3);

        processor.ScrollViewportToTop();

        TerminalViewportScrollState top = processor.ViewportScrollState;
        Assert.Equal(0ul, top.OffsetRows);

        string topRowText = ReadAsciiPrefix(screen, row: 0, columns: 3);
        Assert.NotEqual(bottomRowText, topRowText);

        processor.ScrollViewportByRows(1);
        Assert.Equal(1ul, processor.ViewportScrollState.OffsetRows);

        processor.ScrollViewportToBottom();
        Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);
    }

    [Fact]
    public void GhosttyVtProcessor_KittyGraphicsAndHyperlinks_PopulateManagedScreen_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        GhosttyVtHelpers.GhosttyBuildFeatures features = GhosttyVtHelpers.GetBuildFeatures();
        if (!features.KittyGraphics)
        {
            return;
        }

        TerminalScreen screen = new(columns: 80, viewportRows: 24, scrollbackLimit: 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.NotifyResize(columns: 80, rows: 24, widthPx: 640, heightPx: 384);

        processor.Process("\u001b]8;;https://example.com\u001b\\A\u001b]8;;\u001b\\"u8);
        processor.Process("\u001b_Ga=T,t=d,f=24,i=1,p=1,s=1,v=2,c=10,r=1;////////\u001b\\"u8);

        TerminalCell linkCell = screen.GetViewportRow(0)[0];
        Assert.True(linkCell.HyperlinkId > 0);
        Assert.True(screen.TryGetHyperlinkUrl(linkCell.HyperlinkId, out string? hyperlink));
        Assert.Equal("https://example.com", hyperlink);

        Assert.True(screen.HasKittyGraphics);
        ReadOnlySpan<TerminalKittyImagePlacement> placements = screen.GetKittyPlacements();
        Assert.Single(placements.ToArray());

        TerminalKittyImagePlacement placement = placements[0];
        Assert.Equal(1, placement.ImageId);
        Assert.Equal(TerminalKittyImageLayer.AboveText, placement.Layer);
        Assert.True(placement.WidthPx > 0);
        Assert.True(placement.HeightPx > 0);
        Assert.Equal(1, placement.SourceWidth);
        Assert.Equal(2, placement.SourceHeight);

        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? image));
        Assert.NotNull(image);
        Assert.Equal(1, image!.WidthPx);
        Assert.Equal(2, image.HeightPx);
        Assert.Equal(8, image.RgbaPixels.Length);
        Assert.Equal(0xFF, image.RgbaPixels[3]);
        Assert.Equal(0xFF, image.RgbaPixels[7]);
    }

    [Fact]
    public void GhosttyVtProcessor_FullBufferSearch_FindsScrollbackMatches_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.NotifyResize(columns: 8, rows: 4, widthPx: 64, heightPx: 64);

        StringBuilder builder = new();
        for (int i = 0; i < 10; i++)
        {
            builder.Append("L");
            builder.Append(i.ToString("D2"));
            builder.Append('\n');
        }

        processor.Process(Encoding.UTF8.GetBytes(builder.ToString()));

        List<TerminalSearchMatch> matches = [];
        processor.PopulateSearchMatches("L00", matches);

        Assert.Single(matches);
        Assert.Equal(0, matches[0].AbsoluteRow);
        Assert.Equal(0, matches[0].StartColumn);
        Assert.Equal(2, matches[0].EndColumn);
    }

    [Fact]
    public void GhosttyVtProcessor_SelectionExport_FormatsViewportSelection_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.NotifyResize(columns: 8, rows: 4, widthPx: 64, heightPx: 64);
        processor.Process("ABC\r\nDEF"u8);

        string? selection = ((ITerminalSelectionExportSource)processor)
            .ReadSelection(new TerminalSelectionRange(1, 0, 2, 0));

        Assert.Equal("BC", selection);
    }

    [Fact]
    public void GhosttyVtProcessor_PasteEncoding_UsesNativeGhosttyRules_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using GhosttyVtProcessor processor = new(screen);

        ITerminalPasteSequenceEncoderSource pasteEncoder = processor;
        Assert.False(pasteEncoder.IsPasteSafe("hello\nworld"));
        Assert.True(pasteEncoder.TryEncodePaste("hel\x1blo\x00world", bracketedPaste: true, out byte[] sequence));
        Assert.Equal("\x1b[200~hel lo world\x1b[201~", Encoding.UTF8.GetString(sequence));
    }

    [Fact]
    public void GhosttyVtProcessor_SnapshotExport_SupportsPlainVtAndHtml_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(columns: 8, viewportRows: 4, scrollbackLimit: 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.NotifyResize(columns: 8, rows: 4, widthPx: 64, heightPx: 64);
        processor.Process("\u001b]8;;https://example.com\u001b\\AB\u001b]8;;\u001b\\\r\n"u8);

        ITerminalSnapshotExportSource exporter = processor;
        Assert.True(exporter.SupportsSnapshotFormat(TerminalSnapshotExportFormat.PlainText));
        Assert.True(exporter.SupportsSnapshotFormat(TerminalSnapshotExportFormat.StyledVt));
        Assert.True(exporter.SupportsSnapshotFormat(TerminalSnapshotExportFormat.Html));
        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, new TerminalSnapshotExportOptions(), out string plain));
        Assert.Contains("AB", plain);

        TerminalSnapshotExportOptions vtOptions = new(
            Extras: new TerminalSnapshotExportExtras(
                IncludeCursor: true,
                IncludeHyperlinks: true,
                IncludeKittyKeyboard: true));
        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, vtOptions, out string styled));
        Assert.Contains("AB", styled);
        Assert.Contains("\u001b[2;1H", styled, StringComparison.Ordinal);

        Assert.True(exporter.TryExportSnapshot(TerminalSnapshotExportFormat.Html, new TerminalSnapshotExportOptions(), out string html));
        Assert.Contains("AB", html);
        Assert.Contains('<', html);
    }

    [Fact]
    public void GhosttyFormatter_SelectionRange_FormatsDirectRange_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(columns: 8, rows: 4, maxScrollback: 0);
        terminal.Write("ABC"u8);

        Assert.True(
            terminal.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Active(1, 0), out GhosttyVtNative.GhosttyGridRef startRef));
        Assert.True(
            terminal.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Active(2, 0), out GhosttyVtNative.GhosttyGridRef endRef));

        using GhosttyFormatter formatter = new(
            terminal,
            GhosttyVtNative.GhosttyFormatterFormat.Plain,
            unwrap: false,
            trim: false,
            selection: new RoyalTerminal.GhosttySharp.GhosttySelection(startRef, endRef));

        Assert.Equal("BC", formatter.FormatToString());
    }

    [Fact]
    public void GhosttyTerminal_GridRef_ResolvesRequestedCell_WhenAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        using GhosttyTerminal terminal = new(columns: 8, rows: 4, maxScrollback: 0);
        terminal.Write("ABC"u8);

        Assert.True(
            terminal.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Active(1, 0), out GhosttyVtNative.GhosttyGridRef gridRef));

        unsafe
        {
            uint[] graphemes = new uint[8];
            fixed (uint* graphemePtr = graphemes)
            {
                Assert.Equal(
                    GhosttyVtNative.GhosttyResult.Success,
                    GhosttyVtNative.GridRefGraphemes(in gridRef, graphemePtr, (nuint)graphemes.Length, out nuint written));
                Assert.Equal("B", string.Concat(graphemes.AsSpan(0, checked((int)written)).ToArray().Select(static value => char.ConvertFromUtf32((int)value))));
            }
        }
    }

    private static string ReadAsciiPrefix(TerminalScreen screen, int row, int columns)
    {
        TerminalRow terminalRow = screen.GetViewportRow(row);
        int maxColumns = Math.Min(columns, terminalRow.Columns);
        char[] chars = new char[maxColumns];
        for (int col = 0; col < maxColumns; col++)
        {
            int codepoint = terminalRow[col].Codepoint;
            chars[col] = codepoint <= 0 ? ' ' : codepoint <= 0x7F ? (char)codepoint : '?';
        }

        return new string(chars);
    }
}
