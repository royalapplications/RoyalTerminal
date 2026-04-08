// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Native Ghostty VT processor integration coverage.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
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
