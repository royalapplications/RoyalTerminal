// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty render.zig 4af7e19c7 selects whole wide glyphs, including those
// reached through a right-edge spacer. WT TerminalSelection.cpp adjusts DBCS
// endpoints; xterm.js SelectionService similarly adjusts zero-width tails.
public sealed class WideSelectionRenderingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool native in new[] { false, true })
        foreach (bool spans in new[] { false, true })
        foreach (bool rectangle in new[] { false, true })
            yield return [native, spans, rectangle];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EitherHalfHighlightsTheWholeGlyph(bool native, bool spans, bool rectangle)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(10, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.UTF8.GetBytes("a宽b"));
        using SkiaTerminalRenderer renderer = CreateRenderer();
        renderer.SelectionIsRectangle = rectangle;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(100, 60));
        foreach ((int start, int end) in new[] { (2, 3), (1, 2) })
        {
            Select(renderer, spans, start, end);
            renderer.RenderFull(surface.Canvas, screen);
            using SKImage actual = surface.Snapshot();
            using SKPixmap pixels = actual.PeekPixels();
            AssertCell(pixels, 0, 0, selected: false);
            AssertCell(pixels, 1, 0, selected: true);
            AssertCell(pixels, 2, 0, selected: true);
            AssertCell(pixels, 3, 0, selected: false);

            Select(renderer, spans, 1, 3);
            renderer.RenderFull(surface.Canvas, screen);
            using SKImage expected = surface.Snapshot();
            using SKPixmap expectedPixels = expected.PeekPixels();
            // Covers foreground as well as fill: the leading glyph must not
            // retain its original foreground when only its tail was selected.
            Assert.Equal(expectedPixels.GetPixelSpan().ToArray(), pixels.GetPixelSpan().ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SpacerHeadExtendsOnlyLinearSelectionAndRepaintsWhenOverwritten(bool native, bool spans, bool rectangle)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(10, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.UTF8.GetBytes("abcdefghi宽\x1b[1;10H"));
        Assert.True(screen.GetViewportRow(0).ReadOnlyCells[9].IsWideSpacerHead);
        using SkiaTerminalRenderer renderer = CreateRenderer();
        renderer.SelectionIsRectangle = rectangle;
        Select(renderer, spans, 7, 10);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(100, 60));
        renderer.RenderFull(surface.Canvas, screen);
        using (SKImage image = surface.Snapshot())
        using (SKPixmap pixels = image.PeekPixels())
        {
            AssertCell(pixels, 7, 0, selected: true);
            AssertCell(pixels, 9, 0, selected: true);
            AssertCell(pixels, 0, 1, selected: !rectangle);
            AssertCell(pixels, 1, 1, selected: !rectangle);
            AssertCell(pixels, 2, 1, selected: false);
        }

        processor.Process("Z"u8);
        Assert.False(screen.GetViewportRow(0).ReadOnlyCells[9].IsWideSpacerHead);
        // Isolate the renderer dependency from processors that conservatively
        // invalidate the whole viewport after a cursor or selection change.
        screen.GetViewportRow(1).IsDirty = false;
        screen.GetViewportRow(2).IsDirty = false;
        renderer.Render(surface.Canvas, screen);
        using (SKImage image = surface.Snapshot())
        using (SKPixmap pixels = image.PeekPixels())
        {
            AssertCell(pixels, 0, 1, selected: false);
            AssertCell(pixels, 1, 1, selected: false);
        }
        Assert.False(screen.GetViewportRow(2).IsDirty);
    }

    private static SkiaTerminalRenderer CreateRenderer()
    {
        SkiaTerminalRenderer renderer = new("Consolas", 14f)
        {
            CursorVisible = false,
            EnableBackgroundOpacityHeuristics = false,
            SelectionColor = SKColors.Blue,
            SelectionForegroundColor = SKColors.Lime,
        };
        renderer.SetCellSize(10, 20);
        return renderer;
    }

    private static void Select(SkiaTerminalRenderer renderer, bool spans, int start, int end)
    {
        if (spans) renderer.SetSelectionSpans([new(0, start, end - 1, TerminalHighlightKind.Selection)]);
        else
        {
            renderer.SelectionStart = (end, 0);
            renderer.SelectionEnd = (start, 0);
        }
    }

    private static void AssertCell(SKPixmap pixels, int column, int row, bool selected)
    {
        SKColor color = pixels.GetPixelColor(column * 10 + 5, row * 20);
        if (selected) Assert.Equal(SKColors.Blue, color);
        else Assert.NotEqual(SKColors.Blue, color);
    }
}
