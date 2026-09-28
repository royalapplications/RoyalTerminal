// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalExternalRenderRowsTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(5, 5, 2, 1)]
    [InlineData(0, 5, 0, 1)]
    public void BorrowedViewClampsToCaptureWithoutCreatingScrollback(int above, int below, int actualAbove, int actualBelow)
    {
        TerminalScreen screen = Capture();
        TerminalRenderViewport view = screen.GetRenderViewport(new((ushort)above, (ushort)below));
        Assert.Equal(2, screen.TotalRows);
        Assert.Equal(0, screen.MaxScrollOffset);
        Assert.Equal(2, view.Rows);
        Assert.Equal(new TerminalRenderOverscan((ushort)actualAbove, (ushort)actualBelow), view.CapturedOverscan);
        for (int index = 0; index < view.Count; index++)
        {
            TerminalRenderRow row = view[index];
            Assert.Equal(index - actualAbove, row.ViewportY);
            Assert.Equal(above + row.ViewportY, row.StorageIndex);
            Assert.Equal('c' + row.ViewportY, row.Row.ReadOnlyCells[0].Codepoint);
            if ((uint)row.ViewportY < 2) Assert.Same(screen.GetViewportRow(row.ViewportY), row.Row);
        }
    }

    [Fact]
    public void CowCopyOwnsWrappersAndPublicationTransfersTheWholeCapture()
    {
        TerminalScreen source = Capture();
        TerminalScreen copy = source.CreateStateCopy();
        TerminalRenderOverscan request = new(2, 1);
        for (int index = 0; index < 5; index++)
        {
            TerminalRow original = source.GetRenderViewport(request)[index].Row;
            TerminalRow cloned = copy.GetRenderViewport(request)[index].Row;
            Assert.NotSame(original, cloned);
            Assert.Equal(original.RenderId, cloned.RenderId);
            cloned.Cells[0].Codepoint = 'X';
            Assert.NotEqual(original.RenderId, cloned.RenderId);
            Assert.Equal('a' + index, original.ReadOnlyCells[0].Codepoint);
        }
        TerminalScreen destination = new(4, 2);
        destination.AdoptStateFrom(copy);
        Assert.Equal(5, destination.GetRenderViewport(request).Count);
        for (int index = 0; index < 5; index++)
            Assert.Same(copy.GetRenderViewport(request)[index].Row, destination.GetRenderViewport(request)[index].Row);
        destination.AdoptStateFrom(new TerminalScreen(4, 2));
        Assert.Equal(2, destination.GetRenderViewport(request).Count);
    }

    [Theory]
    [InlineData("resize")]
    [InlineData("reset")]
    [InlineData("grow")]
    [InlineData("alternate")]
    [InlineData("primary")]
    public void ScreenLifecycleDropsAdapterRows(string operation)
    {
        TerminalScreen screen = Capture();
        if (operation == "primary")
        {
            screen.SwitchToAlternateBuffer(clear: true);
            screen.PublishExternalRenderRows(Rows(), 2, 1);
        }
        switch (operation)
        {
            case "resize": screen.Resize(5, 3, reflowOnResize: false); break;
            case "reset": screen.ClearAll(); break;
            case "grow": screen.AddRow(); break;
            case "alternate": screen.SwitchToAlternateBuffer(clear: true); break;
            case "primary": screen.SwitchToPrimaryBuffer(); break;
        }
        Assert.Null(screen.ExternalRenderRows);
    }

    [Fact]
    public void DirtyQueriesAreRestrictedToTheRequestedRange()
    {
        TerminalScreen screen = Capture();
        foreach (TerminalRow row in screen.ExternalRenderRows!) row.IsDirty = false;
        screen.ExternalRenderRows![0].IsDirty = true;
        Assert.False(screen.HasDirtyRows());
        Assert.False(screen.HasDirtyRows(new(1, 1)));
        Assert.True(screen.HasDirtyRows(new(2, 1)));
        screen.InvalidateAll();
        Assert.All(screen.ExternalRenderRows!, row => Assert.True(row.IsDirty));
    }

    [Fact]
    public void InvalidPublicationLeavesPreviousRowsIntact()
    {
        TerminalScreen screen = Capture();
        TerminalRow[] original = screen.ExternalRenderRows!;
        Assert.Throws<ArgumentException>(() => screen.PublishExternalRenderRows(Rows(), 1, 1));
        TerminalRow[] wrongWidth = Rows();
        wrongWidth[^1] = new TerminalRow(5);
        Assert.Throws<ArgumentException>(() => screen.PublishExternalRenderRows(wrongWidth, 2, 1));
        Assert.Same(original, screen.ExternalRenderRows);
        Assert.Same(original[2], screen.GetViewportRow(0));
        screen.AddRow();
        Assert.Throws<ArgumentException>(() => screen.PublishExternalRenderRows(Rows(), 2, 1));
    }

    [Fact]
    public void RepeatedBorrowingDoesNotAllocate()
    {
        TerminalScreen screen = Capture();
        for (int index = 0; index < 100; index++) Read(screen);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int index = 0; index < 100; index++) sum += Read(screen);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(49_500, sum);
        Assert.Equal(0, allocated);

        static int Read(TerminalScreen screen)
        {
            TerminalRenderViewport view = screen.GetRenderViewport(new(2, 1));
            int sum = 0;
            for (int index = 0; index < view.Count; index++) sum += view[index].Row.ReadOnlyCells[0].Codepoint;
            return sum;
        }
    }

    private static TerminalScreen Capture()
    {
        TerminalScreen screen = new(4, 2, 10);
        screen.PublishExternalRenderRows(Rows(), 2, 1);
        return screen;
    }

    private static TerminalRow[] Rows()
    {
        TerminalRow[] rows = new TerminalRow[5];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = new TerminalRow(4);
            rows[index].Cells[0].Codepoint = 'a' + index;
        }
        return rows;
    }
}
