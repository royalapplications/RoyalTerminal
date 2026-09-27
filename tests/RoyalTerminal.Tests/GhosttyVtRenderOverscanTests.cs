// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Requires the rebuilt b40acce58 native library. Reference: Ghostty render.zig
// signed Y / compact capture / ID lifetime, and c/render.zig's delayed request
// publication. WT ROW and xterm BufferLine also reuse mutable row storage: a
// retained ID never permits skipping dirty text/style conversion. Ghostling's
// ordinary iterator remains viewport-only without an explicit extra-row request.
public sealed class GhosttyVtRenderOverscanTests
{
    [Fact]
    public void DefaultIsViewportOnlyAndOptInDoesNotChangeBufferCoordinates()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = History(screen);
        Assert.Equal(default, processor.RenderOverscan);
        Assert.Equal("de", Text(screen));
        Assert.Null(screen.ExternalRenderRows);
        processor.SetViewportOffsetRows(1);
        ITerminalRenderOverscanSink sink = processor;
        sink.RenderOverscan = new(1, 2);
        TerminalRenderViewport view = screen.GetRenderViewport(new(1, 2));
        Assert.Equal("abcde", Text(screen));
        Assert.Equal(2, screen.TotalRows);
        Assert.Equal(0, screen.MaxScrollOffset);
        Assert.Equal("bc", Text(screen, default));
        Assert.Equal(new TerminalRenderOverscan(1, 2), view.CapturedOverscan);
        for (int index = 0; index < view.Count; index++)
        {
            Assert.Equal(index - 1, view[index].ViewportY);
            if (index is 1 or 2) Assert.Same(screen.GetViewportRow(index - 1), view[index].Row);
        }
    }

    [Theory]
    [InlineData(0, 0, 3)]
    [InlineData(1, 1, 2)]
    [InlineData(3, 3, 0)]
    public void CountsClampAtHistoryEdges(int top, int above, int below)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = History(screen);
        processor.RenderOverscan = new(ushort.MaxValue, ushort.MaxValue);
        processor.SetViewportOffsetRows((ulong)top);
        TerminalRenderViewport view = screen.GetRenderViewport(processor.RenderOverscan);
        Assert.Equal(new TerminalRenderOverscan((ushort)above, (ushort)below), view.CapturedOverscan);
        Assert.Equal("abcde", Text(screen));
        Assert.Equal(ushort.MaxValue - above, view[0].StorageIndex);
    }

    [Fact]
    public void NativePageSlackNeverLeaksPastConfiguredHistoryBudget()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 1);
        using GhosttyVtProcessor processor = History(screen);
        processor.RenderOverscan = new(ushort.MaxValue, ushort.MaxValue);
        processor.ScrollViewportToTop();
        Assert.Equal("cde", Text(screen));
        Assert.Equal(new TerminalRenderOverscan(0, 1), screen.GetRenderViewport(processor.RenderOverscan).CapturedOverscan);
        processor.ScrollViewportToBottom();
        Assert.Equal(new TerminalRenderOverscan(1, 0), screen.GetRenderViewport(processor.RenderOverscan).CapturedOverscan);
        screen.ScrollbackLimit = 0;
        processor.Process("\u001b[0m"u8);
        Assert.Equal("de", Text(screen));
    }

    [Fact]
    public void RowsRetainStorageAcrossViewportMovementButStillRefreshContents()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = History(screen);
        processor.RenderOverscan = new(1, 2);
        processor.SetViewportOffsetRows(1);
        TerminalRow b = screen.GetViewportRow(0);
        TerminalRenderRowId id = b.RenderId;
        processor.SetViewportOffsetRows(2);
        Assert.Same(b, screen.GetRenderViewport(processor.RenderOverscan)[0].Row);
        Assert.Equal(id, b.RenderId);
        processor.ScrollViewportToBottom();
        TerminalRow d = screen.GetViewportRow(0);
        id = d.RenderId;
        processor.Process("\u001b[1;1HZ"u8);
        Assert.Equal('Z', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Same(d, screen.GetViewportRow(0));
        Assert.Equal(id, d.RenderId);
    }

    [Fact]
    public void DirtyIteratorUpdatesBelowViewportUsingSignedYNotCaptureIndex()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = History(screen);
        processor.SetViewportOffsetRows(1);
        processor.RenderOverscan = new(1, 2);
        foreach (TerminalRow row in screen.ExternalRenderRows!) row.IsDirty = false;
        processor.Process("\u001b[2;1HZ"u8);
        Assert.Equal("abcdZ", Text(screen));
        Assert.Equal("bc", Text(screen, default));
        Assert.True(screen.GetRenderViewport(processor.RenderOverscan)[4].Row.IsDirty);
        Assert.True(screen.HasDirtyRows(processor.RenderOverscan));
    }

    [Fact]
    public void HyperlinksAndStylesResolveAboveAndBelowTheViewport()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = new(screen);
        for (int row = 0; row < 5; row++)
        {
            string line = $"\u001b[1;38;2;10;20;30m\u001b]8;id=row{row};https://example.test/{row}\u001b\\{(char)('a' + row)}\u001b]8;;\u001b\\";
            processor.Process(Encoding.UTF8.GetBytes(line + (row == 4 ? "" : "\r\n")));
        }
        processor.SetViewportOffsetRows(1);
        processor.RenderOverscan = new(1, 2);
        TerminalRenderViewport view = screen.GetRenderViewport(processor.RenderOverscan);
        Assert.Equal(5, view.Count);
        for (int index = 0; index < view.Count; index++)
        {
            TerminalCell cell = view[index].Row.ReadOnlyCells[0];
            Assert.Equal(0xFF0A141Eu, cell.Foreground);
            Assert.True(screen.TryGetHyperlinkUrl(cell.HyperlinkId, out string? url));
            Assert.Equal($"https://example.test/{index}", url);
        }
    }

    [Fact]
    public void RequestAndDirtyInputRemainUnpublishedDuringRenderHold()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = new(screen, new FrozenClock());
        processor.Process("a\r\nb\r\nc\r\nd\r\ne"u8);
        processor.SetViewportOffsetRows(1);
        processor.RenderOverscan = new(1, 2);
        TerminalRenderRowId id = screen.GetRenderViewport(processor.RenderOverscan)[4].Id;
        processor.Process("\u001b[?2026h\u001b[2;1HZ"u8);
        processor.RenderOverscan = default;
        Assert.Equal("abcde", Text(screen));
        Assert.Equal(id, screen.GetRenderViewport(new(1, 2))[4].Id);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal("bc", Text(screen));
        Assert.Null(screen.ExternalRenderRows);
        processor.RenderOverscan = new(1, 2);
        Assert.Equal("abcdZ", Text(screen));
    }

    [Fact]
    public void AlternateBufferDoesNotExposeIncidentalHistory()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = History(screen);
        processor.RenderOverscan = new(ushort.MaxValue, ushort.MaxValue);
        processor.Process("\u001b[?1049ha\r\nb\r\nc\r\nd"u8);
        Assert.Equal(2, screen.GetRenderViewport(processor.RenderOverscan).Count);
        Assert.Equal(default, screen.GetRenderViewport(processor.RenderOverscan).CapturedOverscan);
        processor.Process("\u001b[?1049l"u8);
        Assert.Equal("abcde", Text(screen));
    }

    [Fact]
    public void ResizeResetAndDisposeReleaseStaleCaptureStorage()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = History(screen);
        processor.RenderOverscan = new(1, 1);
        processor.NotifyResize(6, 3);
        TerminalRenderViewport view = screen.GetRenderViewport(processor.RenderOverscan);
        Assert.Equal(3, view.Rows);
        for (int index = 0; index < view.Count; index++) Assert.Equal(6, view[index].Row.Columns);
        processor.Reset();
        Assert.Equal(new TerminalRenderOverscan(1, 1), processor.RenderOverscan);
        Assert.Equal(3, screen.GetRenderViewport(processor.RenderOverscan).Count);
        processor.Dispose();
        Assert.Null(screen.ExternalRenderRows);
        Assert.Throws<ObjectDisposedException>(() => processor.RenderOverscan = new(2, 2));
    }

    [Fact]
    public void OptInWithNoAvailableOverscanStillPublishesAndCanBeDisabled()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 0);
        using GhosttyVtProcessor processor = new(screen);
        processor.RenderOverscan = new(1, 1);
        Assert.NotNull(screen.ExternalRenderRows);
        Assert.Equal(2, screen.GetRenderViewport(processor.RenderOverscan).Count);
        processor.RenderOverscan = default;
        Assert.Null(screen.ExternalRenderRows);
    }

    [Fact]
    public void NativeCaptureUsesSharedSkiaSignedRowCoordinates()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(4, 2, 10);
        using GhosttyVtProcessor processor = new(screen);
        processor.Process("\u001b[48;2;255;0;0mA\r\n\u001b[48;2;0;255;0mB\r\n\u001b[48;2;0;0;255mC\r\n\u001b[48;2;255;255;0mD\r\nE"u8);
        processor.SetViewportOffsetRows(1);
        processor.RenderOverscan = new(1, 1);
        using SkiaTerminalRenderer renderer = new("Consolas", 14f) { CursorVisible = false };
        renderer.SetCellSize(16, 20);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(64, 80));
        surface.Canvas.Translate(0, 20);
        renderer.Render(surface.Canvas, screen, processor.RenderOverscan, forceFullRedraw: true);
        using SKImage image = surface.Snapshot();
        using SKPixmap pixels = image.PeekPixels();
        SKColor[] colors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow];
        for (int index = 0; index < colors.Length; index++)
        {
            Assert.Equal(colors[index], pixels.GetPixelColor(1, index * 20 + 1));
            Assert.False(screen.GetRenderViewport(processor.RenderOverscan)[index].Row.IsDirty);
        }
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    private static GhosttyVtProcessor History(TerminalScreen screen)
    {
        GhosttyVtProcessor processor = new(screen);
        processor.Process("a\r\nb\r\nc\r\nd\r\ne"u8);
        return processor;
    }

    private static string Text(TerminalScreen screen) => Text(screen, new(ushort.MaxValue, ushort.MaxValue));

    private static string Text(TerminalScreen screen, TerminalRenderOverscan request)
    {
        TerminalRenderViewport view = screen.GetRenderViewport(request);
        StringBuilder result = new(view.Count);
        for (int index = 0; index < view.Count; index++) result.Append((char)view[index].Row.ReadOnlyCells[0].Codepoint);
        return result.ToString();
    }
}
