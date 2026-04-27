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
        Assert.Equal(6, source.HeightPx);
        Assert.Equal(1, processor.CursorCol);
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
        processor.Process(Encoding.ASCII.GetBytes(RedPixelSixel));

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
