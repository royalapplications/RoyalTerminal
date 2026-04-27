// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Managed VT sixel integration tests.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSixelVtProcessorTests
{
    private const string RedPixelSixel = "\u001bPq#1;2;100;0;0#1@\u001b\\";

    [Fact]
    public void BasicVtProcessor_ManagedSixelDisabled_IgnoresSixelPayload()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

        Assert.False(screen.HasRasterGraphics);
        Assert.True(screen.GetRasterImagePlacements().IsEmpty);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_AddsRasterPlacement()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

        Assert.True(screen.HasRasterGraphics);
        ReadOnlySpan<TerminalRasterImagePlacement> placements = screen.GetRasterImagePlacements();
        Assert.Equal(1, placements.Length);
        TerminalRasterImagePlacement placement = placements[0];
        Assert.Equal(TerminalRasterImageLayer.BelowText, placement.Layer);
        Assert.Equal(0, placement.AnchorColumn);
        Assert.Equal(screen.GetAbsoluteRowForViewportRow(0), placement.AnchorRow);
        Assert.True(screen.TryGetRasterImageSource(placement.ImageId, out TerminalRasterImageSource? source));
        Assert.Equal(TerminalRasterImageProtocol.Sixel, source!.Protocol);
        Assert.Equal(1, source.WidthPx);
        Assert.Equal(12, source.HeightPx);
        Assert.Equal(1, processor.CursorCol);
    }

    [Fact]
    public void BasicVtProcessor_DecSixelDisplayMode_RendersFromViewportHome_AndPreservesCursor()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process(Encoding.ASCII.GetBytes("\u001b[2;4H\u001b[?80h" + RedPixelSixel));

        ReadOnlySpan<TerminalRasterImagePlacement> placements = screen.GetRasterImagePlacements();
        Assert.Equal(1, placements.Length);
        Assert.Equal(0, placements[0].AnchorColumn);
        Assert.Equal(screen.GetAbsoluteRowForViewportRow(0), placements[0].AnchorRow);
        Assert.Equal(3, processor.CursorCol);
        Assert.Equal(1, processor.CursorRow);
    }

    [Fact]
    public void BasicVtProcessor_DecSixelDisplayMode_QueryReportsStateWhenSixelEnabled()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        List<string> responses = [];
        processor.ResponseCallback = bytes => responses.Add(Encoding.ASCII.GetString(bytes));

        processor.Process("\u001b[?80$p"u8);
        processor.Process("\u001b[?80h\u001b[?80$p"u8);

        Assert.Equal("\u001b[?80;2$y", responses[0]);
        Assert.Equal("\u001b[?80;1$y", responses[1]);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_ClearsTextUnderNewPlacement()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process(Encoding.ASCII.GetBytes("abc\r\u001bPq#1;2;100;0;0#1!15@\u001b\\"));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.False(row[0].HasContent);
        Assert.False(row[1].HasContent);
        Assert.Equal('c', row[2].Codepoint);
        Assert.True(screen.HasRasterGraphics);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_TextWriteClearsIntersectingPlacement()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

        processor.Process("\rX"u8);

        Assert.False(screen.HasRasterGraphics);
        Assert.Equal('X', screen.GetViewportRow(0)[0].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelDisabled_ClearsExistingRasterPlacement()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

        processor.SixelGraphicsEnabled = false;

        Assert.False(screen.HasRasterGraphics);
        Assert.True(screen.GetRasterImagePlacements().IsEmpty);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_ReplacesIntersectingFramePlacement()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));
        processor.Process(Encoding.ASCII.GetBytes("\r\u001bPq#2;2;0;100;0#2@\u001b\\"));

        ReadOnlySpan<TerminalRasterImagePlacement> placements = screen.GetRasterImagePlacements();
        Assert.Equal(1, placements.Length);
        Assert.True(screen.TryGetRasterImageSource(placements[0].ImageId, out TerminalRasterImageSource? source));
        Assert.Equal([0x00, 0xFF, 0x00, 0xFF], source!.RgbaPixels.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_AllowsWrappedSixelPayload()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process(Encoding.ASCII.GetBytes("\u001bPq#1;2;100;0;0#1@\n\u001b\\"));

        Assert.True(screen.HasRasterGraphics);
        Assert.Equal(1, screen.GetRasterImagePlacements().Length);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_CanBeClearedByEraseDisplay()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

        processor.Process("\u001b[2J"u8);

        Assert.False(screen.HasRasterGraphics);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_CanBeClearedByEraseLine()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));
        processor.Process("\r"u8);

        processor.Process("\u001b[K"u8);

        Assert.False(screen.HasRasterGraphics);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_TrimmedWhenScrollbackDropsAnchor()
    {
        TerminalScreen screen = new(10, 2, 0);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 2, 100, 20);
        processor.Process(Encoding.ASCII.GetBytes("\u001bPq\"1;1;1;6#1;2;100;0;0#1@\u001b\\"));

        processor.Process("\n\n"u8);

        Assert.False(screen.HasRasterGraphics);
    }

    [Fact]
    public void BasicVtProcessor_ManagedSixelEnabled_ReflowResizePreservesRasterPlacement()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen)
        {
            SixelGraphicsEnabled = true,
        };
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

        processor.ResizeScreen(columns: 5, rows: 4, widthPx: 100, heightPx: 40, reflowOnResize: true);

        Assert.True(screen.HasRasterGraphics);
        Assert.Equal(1, screen.GetRasterImagePlacements().Length);
    }
}
