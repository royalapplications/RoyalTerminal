// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Glyphs;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalGlyphRenderingTests(ITestOutputHelper output)
{
    private const string Triangle = "AAEAZABkA4QDhAACAAABAQEB9P5wAyADhPzgAAA=";
    private const string Empty = "AAAAAAAAAAAAAA==";

    [Theory]
    [InlineData(TerminalGlyphSize.Height, 4, 4, 32, 32)]
    [InlineData(TerminalGlyphSize.Advance, 4, 4, 32, 32)]
    [InlineData(TerminalGlyphSize.Contain, 2, 12, 16, 16)]
    [InlineData(TerminalGlyphSize.Cover, 2, 12, 16, 16)]
    [InlineData(TerminalGlyphSize.Stretch, 2, 4, 16, 32)]
    public void PlacementUsesAuthoredBoxAndCurrentGhosttyAliases(TerminalGlyphSize size, double x, double y, double width, double height)
    {
        TerminalGlyphRegistration glyph = Create(size);
        Assert.Equal(new(100, 100, 900, 900), glyph.Outline.Bounds);
        Assert.True(TerminalGlyphGeometry.TryPlace(glyph, 20, 40, 1, out var placement));
        Assert.Equal(new(x, y, width, height), placement);
    }

    [Fact]
    public void PaddingAlignmentAndWideFaceStretchMatchGhosttyConstraints()
    {
        TerminalGlyphRegistration glyph = Create(TerminalGlyphSize.Stretch,
            new(TerminalGlyphSize.Stretch, TerminalGlyphAlignment.End, TerminalGlyphAlignment.Baseline, .1, .2, .3, .1));
        Assert.True(TerminalGlyphGeometry.TryPlace(glyph, 20, 40, 1, out var placement));
        Assert.Equal(3.4, placement.X, 8);
        Assert.Equal(6.4, placement.Y, 8);
        Assert.Equal(11.2, placement.Width, 8);
        Assert.Equal(19.2, placement.Height, 8);
        glyph = Create(TerminalGlyphSize.Stretch);
        Assert.True(TerminalGlyphGeometry.TryPlace(glyph, 40, 40, 2, out placement));
        Assert.Equal(new(4, 4, 32, 32), placement);
        Assert.True(TerminalGlyphGeometry.TryPlace(glyph, 20, 40, 2, out placement));
        Assert.Equal(new(4, 4, 32, 32), placement);
    }

    [Fact]
    public void InvalidMetricsAndEmptyOutlinesDoNotRenderAndBoundsAvoidIntegerOverflow()
    {
        TerminalGlyphRegistration glyph = Create(TerminalGlyphSize.Stretch);
        foreach (double width in new[] { double.NaN, double.PositiveInfinity, 0, -1 })
            Assert.False(TerminalGlyphGeometry.TryPlace(glyph, width, 40, 1, out _));
        Assert.False(TerminalGlyphGeometry.TryPlace(glyph, 20, 0, 1, out _));
        Assert.False(TerminalGlyphGeometry.TryPlace(glyph, 20, 40, 0, out _));
        Assert.False(TerminalGlyphGeometry.TryPlace(glyph, 20, 40, 3, out _));
        TerminalGlyphOutline empty = new([], []);
        Assert.False(TerminalGlyphGeometry.TryPlace(new(empty, 1000, 1000, 1000, 1, glyph.Layout), 20, 40, 1, out _));
        Assert.Equal(default, empty.Bounds);
        Assert.Equal(4294967295d, new TerminalGlyphBounds(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue).Width);
        Assert.Throws<ArgumentNullException>(() => TerminalGlyphGeometry.TryPlace(null!, 20, 40, 1, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothProcessorsRenderRegisteredGlyphsAndInvalidateCachedPathsAtPublication(bool native)
    {
        if (native)
        {
            bool available = GhosttyVtProcessor.IsAvailable();
            output.WriteLine($"Native glyph rendering available: {available}");
            if (!available) return;
        }
        TerminalScreen screen = new(3, 2);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        using SkiaTerminalRenderer renderer = new("Consolas", 14) { CursorVisible = false };
        renderer.SetCellSize(20, 40);
        using SKBitmap bitmap = new(60, 80);
        using SKCanvas canvas = new(bitmap);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[48;2;0;0;0m\u001b[2J" + Wire("r;cp=e000;size=stretch;width=2;" + Triangle) + "\u001b[38;2;255;0;0m\ue000"));
        renderer.RenderFull(canvas, screen);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(10, 20));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(2, 2));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(30, 20));
        Assert.Equal(1, processor.CursorCol); // Registration metadata never overwrites the actual cell width.
        Assert.Equal(1, renderer.RegisteredGlyphPathCount);
        ulong revision = screen.GlyphRevision;
        processor.Process(Encoding.ASCII.GetBytes("\u001b[?2026h" + Wire("r;cp=e000;" + Empty)));
        Assert.Equal(revision, screen.GlyphRevision);
        renderer.RenderFull(canvas, screen);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(10, 20));
        processor.Process("\u001b[?2026l"u8);
        Assert.NotEqual(revision, screen.GlyphRevision);
        renderer.Render(canvas, screen);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(10, 20));
        Assert.Equal(0, renderer.RegisteredGlyphPathCount);
        processor.Process(Encoding.ASCII.GetBytes(Wire("r;cp=e000;size=stretch;" + Triangle)));
        renderer.CursorVisible = true;
        renderer.CursorColumn = 0;
        renderer.CursorRow = 0;
        renderer.CursorColor = SKColors.Blue;
        renderer.CursorTextColor = SKColors.Lime;
        renderer.RenderFull(canvas, screen);
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(10, 20));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(2, 2));
        Assert.Equal(1, renderer.RegisteredGlyphPathCount);
        processor.Process(Encoding.ASCII.GetBytes(Wire("c")));
        renderer.RenderFull(canvas, screen);
        Assert.Equal(0, renderer.RegisteredGlyphPathCount);
    }

    [Fact]
    public void ScreenSwitchAndRendererDisposalReleaseCachedGlyphPaths()
    {
        TerminalScreen screen = new(2, 1);
        using BasicVtProcessor processor = new(screen);
        using SkiaTerminalRenderer renderer = new("Consolas", 14) { CursorVisible = false };
        using SKBitmap bitmap = new(100, 100);
        using SKCanvas canvas = new(bitmap);
        processor.Process(Encoding.UTF8.GetBytes(Wire("r;cp=e000;" + Triangle) + "\ue000"));
        renderer.RenderFull(canvas, screen);
        Assert.Equal(1, renderer.RegisteredGlyphPathCount);
        renderer.RenderFull(canvas, new TerminalScreen(2, 1));
        Assert.Equal(0, renderer.RegisteredGlyphPathCount);
        renderer.RenderFull(canvas, screen);
        Assert.Equal(1, renderer.RegisteredGlyphPathCount);
        renderer.Dispose();
        Assert.Equal(0, renderer.RegisteredGlyphPathCount);
    }

    [Fact]
    public void NormalizedPathsRetainSmallDetailsAtLargeDesignCoordinates()
    {
        TerminalGlyphOutline outline = new([2],
            [new(int.MaxValue - 2, int.MaxValue - 2, true),
             new(int.MaxValue, int.MaxValue - 2, true), new(int.MaxValue - 1, int.MaxValue, true)]);
        using SKPath path = SkiaTerminalGlyphPath.CreateNormalized(outline);
        Assert.Equal(new SKRect(0, 0, 1, 1), path.Bounds);
        Assert.True(path.Contains(.5f, .5f));
        Assert.False(path.Contains(.1f, .9f));
    }

    [Fact]
    public void OversizedHeightGlyphCannotPaintItsNeighborAndClustersKeepFontPath()
    {
        TerminalScreen screen = new(3, 1);
        using BasicVtProcessor processor = new(screen);
        using SkiaTerminalRenderer renderer = new("Consolas", 14) { CursorVisible = false };
        renderer.SetCellSize(20, 40);
        using SKBitmap bitmap = new(60, 40);
        using SKCanvas canvas = new(bitmap);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[48;2;0;0;0m\u001b[2J" + Wire("r;cp=e000;size=height;" + Triangle) + "\u001b[38;2;255;0;0m\ue000"));
        renderer.RenderFull(canvas, screen);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(19, 30));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(21, 30));
        processor.Process("\u001b[2J\u001b[H"u8);
        processor.Process(Encoding.UTF8.GetBytes("\ue000\u0301"));
        // Switch screens to discard the prior cached scalar path before testing the cluster.
        renderer.RenderFull(canvas, new TerminalScreen(3, 1));
        renderer.RenderFull(canvas, screen);
        Assert.Equal(0, renderer.RegisteredGlyphPathCount);
    }

    private static TerminalGlyphRegistration Create(TerminalGlyphSize size, TerminalGlyphLayout? layout = null)
    {
        Assert.True(TerminalGlyphDecoder.TryDecode(Convert.FromBase64String(Triangle), out var outline, out _));
        return new(outline, 1000, 1000, 1000, 1,
            layout ?? new(size, TerminalGlyphAlignment.Center, TerminalGlyphAlignment.Center, 0, 0, 0, 0));
    }

    private static string Wire(string command) => "\u001b_25a1;" + command + "\u001b\\";
}
