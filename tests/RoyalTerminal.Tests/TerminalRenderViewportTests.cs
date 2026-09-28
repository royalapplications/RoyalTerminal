// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty render.zig / C render.c contracts: signed viewport Y, requested versus
/// actual overscan and opaque storage IDs. WT ROW.Reset/CopyFrom and xterm.js
/// BufferLine reuse mutable storage too, so identity must never replace content
/// validation. Ghostling's current iterator remains zero-overscan by default.
/// </summary>
public sealed class TerminalRenderViewportTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 2, 2, 2, 0)]
    [InlineData(0, 5, 5, 4, 0)]
    [InlineData(4, 5, 5, 0, 4)]
    [InlineData(2, 1, 1, 1, 1)]
    [InlineData(2, 5, 5, 2, 2)]
    [InlineData(2, 65535, 65535, 2, 2)]
    public void CapturesOnlyExistingRowsWithStableRequestedSlots(int scroll, int above, int below, int actualAbove, int actualBelow)
    {
        TerminalScreen screen = CreateHistory();
        screen.ScrollOffset = scroll;
        TerminalRenderOverscan request = new((ushort)above, (ushort)below);
        TerminalRenderViewport rows = screen.GetRenderViewport(request);
        Assert.Equal(4, rows.Columns);
        Assert.Equal(3, rows.Rows);
        Assert.Equal(request, rows.RequestedOverscan);
        Assert.Equal(new TerminalRenderOverscan((ushort)actualAbove, (ushort)actualBelow), rows.CapturedOverscan);
        Assert.Equal(actualAbove, rows.ViewportStart);
        Assert.Equal(3 + actualAbove + actualBelow, rows.Count);
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            Assert.Equal(index - actualAbove, row.ViewportY);
            Assert.Equal(above + row.ViewportY, row.StorageIndex);
            Assert.Same(screen.GetRow(screen.ViewportTopAbsoluteRow + row.ViewportY), row.Row);
            Assert.True(row.Id.IsValid);
            Assert.Equal(row.Row.RenderId, row.Id);
            Assert.Equal('A' + screen.ViewportTopAbsoluteRow + row.ViewportY, row.Row.ReadOnlyCells[0].Codepoint);
        }
        Assert.Equal(scroll, screen.ScrollOffset);
    }

    [Fact]
    public void NewRequestDoesNotModifyAnExistingBorrowedView()
    {
        TerminalScreen screen = CreateHistory();
        screen.ScrollOffset = 2;
        TerminalRenderViewport first = screen.GetRenderViewport(new(1, 1));
        TerminalRenderViewport second = screen.GetRenderViewport(new(5, 5));
        Assert.Equal(5, first.Count);
        Assert.Equal(7, second.Count);
        Assert.Equal(first[0].Id, second[1].Id);
    }

    [Fact]
    public void DefaultAndInvalidIndicesAreSafe()
    {
        TerminalRenderViewport empty = default;
        Assert.Equal(0, empty.Count);
        Assert.Equal(default, empty.RequestedOverscan);
        Assert.Throws<ArgumentOutOfRangeException>(ReadDefault);
        TerminalScreen screen = CreateHistory();
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadAt(screen, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadAt(screen, 3));

        static void ReadDefault() { TerminalRenderViewport view = default; _ = view[0]; }
        static void ReadAt(TerminalScreen screen, int index) { _ = screen.GetRenderViewport()[index]; }
    }

    [Fact]
    public void IdsFollowStorageNotPositionOrContents()
    {
        TerminalScreen screen = CreateHistory();
        TerminalRenderRowId id = screen.GetRenderViewport()[0].Id;
        screen.ScrollOffset = 1;
        Assert.Equal(id, screen.GetRenderViewport()[1].Id);
        TerminalRow row = screen.GetViewportRow(1);
        row.Cells[0].Codepoint = 'X';
        Assert.Equal(id, row.RenderId);
        row.Clear();
        Assert.Equal(id, row.RenderId);
        row.Resize(2);
        Assert.Equal(id, row.RenderId);
        row.Resize(8);
        Assert.NotEqual(id, row.RenderId);
    }

    [Fact]
    public void CowDetachmentAndStorageSwapsHaveDistinctIdentities()
    {
        TerminalRow source = new(4);
        TerminalRow copy = source.CreateStateCopy();
        TerminalRenderRowId shared = source.RenderId;
        Assert.Equal(shared, copy.RenderId);
        copy.Cells[0].Codepoint = 'X';
        Assert.NotEqual(shared, copy.RenderId);
        Assert.Equal(shared, source.RenderId);
        TerminalRenderRowId detached = copy.RenderId;
        source.SwapActiveStorage(copy);
        Assert.Equal(detached, source.RenderId);
        Assert.Equal(shared, copy.RenderId);
        Assert.NotEqual(new TerminalRow(4).RenderId, shared);
    }

    [Fact]
    public void OpaqueIdsSupportTypedEqualityAndDictionaryKeys()
    {
        TerminalRenderRowId invalid = default;
        TerminalRenderRowId id = new TerminalRow(4).RenderId;
        Assert.False(invalid.IsValid);
        Assert.True(id.IsValid);
        Assert.NotEqual(invalid, id);
        TerminalRenderRowId same = id;
        Assert.True(id == same);
        Assert.True(id != invalid);
        Assert.True(id.Equals((object)id));
        Assert.False(id.Equals("not a row"));
        Dictionary<TerminalRenderRowId, int> cache = new() { [id] = 7 };
        Assert.Equal(7, cache[id]);
        Assert.Equal(0, invalid.GetHashCode());
    }

    [Fact]
    public void OverscanExcludesNonScrollableAlternateHistory()
    {
        TerminalScreen screen = new(5, 2, 100);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[?1049habcdefghij\u001b[22J"u8);
        Assert.True(screen.TotalRows > screen.ViewportRows);
        TerminalRenderViewport view = screen.GetRenderViewport(new(100, 100));
        Assert.Equal(default, view.CapturedOverscan);
        Assert.Equal(screen.ViewportRows, view.Count);
    }

    [Fact]
    public void ViewCreationAndTraversalAllocateNothing()
    {
        TerminalScreen screen = CreateHistory();
        screen.ScrollOffset = 2;
        _ = screen.GetRenderViewport(new(65535, 65535));
        long before = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int sample = 0; sample < 1000; sample++)
        {
            TerminalRenderViewport view = screen.GetRenderViewport(new(65535, 65535));
            for (int index = 0; index < view.Count; index++) checksum += view[index].Row.ReadOnlyCells[0].Codepoint;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(476_000, checksum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AvailableRowsAndSignedYMatchNativeRenderCapture()
    {
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable; rebuild required before final validation.");
        TerminalScreen screen = CreateHistory();
        using GhosttyTerminal native = new(4, 3);
        using GhosttyRenderState capture = new();
        native.Write("A\r\nB\r\nC\r\nD\r\nE\r\nF\r\nG"u8);
        foreach (int offset in new[] { 0, 2, 4 })
        {
            screen.ScrollOffset = offset;
            native.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.AbsoluteRow((nuint)screen.ViewportTopAbsoluteRow));
            foreach (TerminalRenderOverscan request in new TerminalRenderOverscan[] { default, new(1, 1), new(2, 3) })
            {
                capture.SetOverscan(new() { Above = request.Above, Below = request.Below });
                capture.Update(native);
                TerminalRenderViewport expected = screen.GetRenderViewport(request);
                var actual = capture.GetCapturedOverscan();
                Assert.Equal(expected.CapturedOverscan.Above, actual.Above);
                Assert.Equal(expected.CapturedOverscan.Below, actual.Below);
                capture.BeginRows();
                int index = 0;
                while (capture.MoveNextRow())
                {
                    Assert.Equal(expected[index].ViewportY, capture.GetCurrentRowViewportY());
                    Assert.NotEqual(default, capture.GetCurrentRowId());
                    index++;
                }
                Assert.Equal(expected.Count, index);
            }
        }
    }

    private static TerminalScreen CreateHistory()
    {
        TerminalScreen screen = new(4, 3, 100);
        for (int i = 0; i < 4; i++) screen.AddRow();
        for (int i = 0; i < screen.TotalRows; i++) screen.GetRow(i).Cells[0].Codepoint = 'A' + i;
        return screen;
    }
}
