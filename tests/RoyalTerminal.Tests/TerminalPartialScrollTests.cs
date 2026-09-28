// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPartialScrollTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    public void RotationPreservesStatusStorageAndAnchorsThroughHistoryPruning(int history)
    {
        TerminalScreen screen = new(8, 6, history);
        TerminalRow status = screen.GetViewportRow(4);
        status[0].Codepoint = 'E';
        status.WrapsToNext = true;
        TerminalScreenAnchor statusAnchor = screen.CreateAnchor(4, 2);
        TerminalScreenAnchor contentAnchor = screen.CreateAnchor(0, 1);
        for (int i = 0; i < 4; i++)
        {
            TerminalRow blank = GrowAndRotateSuffix(screen, 3);
            Assert.Same(blank, screen.GetViewportRow(3));
            Assert.Same(status, screen.GetViewportRow(4));
            Assert.True(status.WrapsToNext);
            Assert.Equal('E', status.ReadOnlyCells[0].Codepoint);
            Assert.True(screen.TryResolveAnchor(statusAnchor, out TerminalGridPosition position));
            Assert.Equal(screen.TotalRows - 2, position.Row);
        }
        Assert.Equal(history >= 4, screen.TryResolveAnchor(contentAnchor, out _));
        Assert.Equal(6 + Math.Min(4, history), screen.TotalRows);
    }

    [Fact]
    public void RotationKeepsRasterAnchorsBelowMarginStationary()
    {
        TerminalScreen screen = new(8, 6, 1);
        for (int row = 2; row <= 4; row += 2)
        {
            int id = screen.AllocateRasterImageId();
            screen.ReplaceRasterImage(
                new TerminalRasterImageSource(id, TerminalRasterImageProtocol.Sixel, 1, 1, new byte[4]),
                new TerminalRasterImagePlacement(id, TerminalRasterImageLayer.BelowText,
                    0, row, 0, 0, 1, 1, 0, 0, 1, 1, 10, 10));
        }
        GrowAndRotateSuffix(screen, 3);
        Assert.Equal(2, screen.GetRasterImagePlacements()[0].AnchorRow);
        Assert.Equal(5, screen.GetRasterImagePlacements()[1].AnchorRow);
        GrowAndRotateSuffix(screen, 3);
        Assert.Equal(1, screen.GetRasterImagePlacements()[0].AnchorRow);
        Assert.Equal(5, screen.GetRasterImagePlacements()[1].AnchorRow);
    }

    [Fact]
    public void WarmTopOriginScrollRecyclesRowsWithoutAllocating()
    {
        TerminalScreen screen = new(80, 24, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1;20r\u001b[999S"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) processor.Process("\u001b[S"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(26, screen.TotalRows);
    }

    private static TerminalRow GrowAndRotateSuffix(TerminalScreen screen, int bottom)
    {
        TerminalRow blank = screen.AddRow();
        screen.ShiftAnchorsBelowHistoryMargin(bottom);
        screen.RotateViewportRowsDown(bottom, screen.ViewportRows - 1);
        return blank;
    }
}
