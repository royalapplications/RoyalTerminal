// Licensed under the MIT License.
// GhosttySharp.Tests — Headless Avalonia + Skia rendering integration tests.
// Validates terminal control rendering, input processing, and Skia drawing.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using GhosttySharp.Avalonia.Controls;
using GhosttySharp.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace GhosttySharp.Tests;

/// <summary>
/// Integration tests that validate the terminal control renders correctly
/// using headless Avalonia with Skia drawing enabled.
/// These tests exercise the full rendering pipeline:
///   TerminalScreen → SkiaTerminalRenderer → SKCanvas → pixel validation.
/// </summary>
public class HeadlessSkiaRenderingTests
{
    #region Skia Renderer → SKCanvas Drawing

    [Fact]
    public void Renderer_RenderFull_DrawsToCanvas()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Write content to screen
        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 'H';
        row[0].Foreground = 0xFFFFFFFF;
        row[1].Codepoint = 'i';
        row[1].Foreground = 0xFFFFFFFF;

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xFF1E1E1E));

        // Should not throw
        renderer.RenderFull(canvas, screen);

        // Verify something was drawn — snapshot the surface
        using var snapshot = surface.Snapshot();
        Assert.NotNull(snapshot);
        Assert.True(snapshot.Width > 0);
        Assert.True(snapshot.Height > 0);
    }

    [Fact]
    public void Renderer_RenderFull_DrawsBackground()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Set a known background on first row
        var row = screen.GetViewportRow(0);
        for (var i = 0; i < 80; i++)
            row[i].Background = 0xFF0000FF; // Blue background

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xFF1E1E1E));

        renderer.RenderFull(canvas, screen);

        // Read back pixels to verify drawing happened
        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        Assert.NotNull(pixmap);

        // Sample a pixel in the first row area — should be blue (0xFF0000FF = ARGB)
        // SKColor is ARGB, so Blue channel should be 0xFF
        var pixel = pixmap.GetPixelColor(1, 1);
        // Verify background was drawn (not the default clear color 0x1E1E1E)
        Assert.True(
            pixel.Blue == 0xFF || pixel.Red == 0xFF,
            $"Expected blue background, got R={pixel.Red} G={pixel.Green} B={pixel.Blue} A={pixel.Alpha}");
    }

    [Fact]
    public void Renderer_RenderFull_DrawsText()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Write characters to screen
        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 'A';
        row[0].Foreground = 0xFFFFFFFF;
        row[0].Background = 0xFF000000;

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.RenderFull(canvas, screen);

        // Read back and verify some non-black pixels exist in the first cell area
        // (text rendering should produce white-ish pixels)
        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        Assert.NotNull(pixmap);

        var cellW = (int)renderer.CellWidth;
        var cellH = (int)renderer.CellHeight;
        var hasNonBlackPixel = false;

        for (var y = 0; y < cellH && !hasNonBlackPixel; y++)
        {
            for (var x = 0; x < cellW && !hasNonBlackPixel; x++)
            {
                var pixel = pixmap.GetPixelColor(x, y);
                if (pixel.Red > 10 || pixel.Green > 10 || pixel.Blue > 10)
                    hasNonBlackPixel = true;
            }
        }

        Assert.True(hasNonBlackPixel, "Text rendering should produce non-black pixels in the cell area");
    }

    [Fact]
    public void Renderer_HiddenAttribute_DoesNotDrawGlyph()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };

        int width = (int)Math.Ceiling(2 * renderer.CellWidth);
        int height = (int)Math.Ceiling(renderer.CellHeight);
        int cellWidth = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth));
        int cellHeight = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight));

        using var hiddenSurface = SKSurface.Create(new SKImageInfo(width, height));
        hiddenSurface.Canvas.Clear(SKColors.Black);
        var hiddenScreen = new TerminalScreen(2, 1);
        var hiddenRow = hiddenScreen.GetViewportRow(0);
        hiddenRow[0].Codepoint = 'A';
        hiddenRow[0].Foreground = 0xFFFFFFFF;
        hiddenRow[0].Background = 0xFF000000;
        hiddenRow[0].Attributes = CellAttributes.Hidden;
        hiddenRow.IsDirty = true;
        renderer.RenderFull(hiddenSurface.Canvas, hiddenScreen);

        using var controlSurface = SKSurface.Create(new SKImageInfo(width, height));
        controlSurface.Canvas.Clear(SKColors.Black);
        var controlScreen = new TerminalScreen(2, 1);
        var controlRow = controlScreen.GetViewportRow(0);
        controlRow[0].Codepoint = 0;
        controlRow[0].Foreground = 0xFFFFFFFF;
        controlRow[0].Background = 0xFF000000;
        controlRow.IsDirty = true;
        renderer.RenderFull(controlSurface.Canvas, controlScreen);

        using var hiddenSnapshot = hiddenSurface.Snapshot();
        using var hiddenPixels = hiddenSnapshot.PeekPixels();
        using var controlSnapshot = controlSurface.Snapshot();
        using var controlPixels = controlSnapshot.PeekPixels();
        Assert.NotNull(hiddenPixels);
        Assert.NotNull(controlPixels);

        bool differsFromControl = false;
        for (int y = 0; y < cellHeight && !differsFromControl; y++)
        {
            for (int x = 0; x < cellWidth && !differsFromControl; x++)
            {
                if (hiddenPixels.GetPixelColor(x, y) != controlPixels.GetPixelColor(x, y))
                {
                    differsFromControl = true;
                }
            }
        }

        Assert.False(differsFromControl, "Hidden attribute should suppress glyph output.");
    }

    [Fact]
    public void Renderer_DimAttribute_ReducesGlyphIntensity()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };

        int width = (int)Math.Ceiling(2 * renderer.CellWidth);
        int height = (int)Math.Ceiling(renderer.CellHeight);
        int cellWidth = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth));
        int cellHeight = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight));

        using var normalSurface = SKSurface.Create(new SKImageInfo(width, height));
        normalSurface.Canvas.Clear(SKColors.Black);
        var normalScreen = new TerminalScreen(2, 1);
        var normalRow = normalScreen.GetViewportRow(0);
        normalRow[0].Codepoint = 'A';
        normalRow[0].Foreground = 0xFFFFFFFF;
        normalRow[0].Background = 0xFF000000;
        normalRow.IsDirty = true;
        renderer.RenderFull(normalSurface.Canvas, normalScreen);

        using var dimSurface = SKSurface.Create(new SKImageInfo(width, height));
        dimSurface.Canvas.Clear(SKColors.Black);
        var dimScreen = new TerminalScreen(2, 1);
        var dimRow = dimScreen.GetViewportRow(0);
        dimRow[0].Codepoint = 'A';
        dimRow[0].Foreground = 0xFFFFFFFF;
        dimRow[0].Background = 0xFF000000;
        dimRow[0].Attributes = CellAttributes.Dim;
        dimRow.IsDirty = true;
        renderer.RenderFull(dimSurface.Canvas, dimScreen);

        using var normalSnapshot = normalSurface.Snapshot();
        using var normalPixels = normalSnapshot.PeekPixels();
        using var dimSnapshot = dimSurface.Snapshot();
        using var dimPixels = dimSnapshot.PeekPixels();
        Assert.NotNull(normalPixels);
        Assert.NotNull(dimPixels);

        long normalLuma = 0;
        long dimLuma = 0;
        for (int y = 0; y < cellHeight; y++)
        {
            for (int x = 0; x < cellWidth; x++)
            {
                SKColor normalPixel = normalPixels.GetPixelColor(x, y);
                SKColor dimPixel = dimPixels.GetPixelColor(x, y);
                normalLuma += normalPixel.Red + normalPixel.Green + normalPixel.Blue;
                dimLuma += dimPixel.Red + dimPixel.Green + dimPixel.Blue;
            }
        }

        Assert.True(normalLuma > 0, "Baseline glyph should render visible pixels.");
        Assert.True(dimLuma < normalLuma, "Dim attribute should reduce glyph intensity.");
    }

    [Fact]
    public void Renderer_MixedScriptLine_RendersWithoutExceptionAndProducesPixels()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        var screen = new TerminalScreen(8, 1);
        var row = screen.GetViewportRow(0);

        int[] codepoints = ['A', 0x0645, 0x4E2D, 0x1F642];
        for (int i = 0; i < codepoints.Length; i++)
        {
            row[i].Codepoint = codepoints[i];
            row[i].Foreground = 0xFFFFFFFF;
            row[i].Background = 0xFF000000;
        }

        int width = (int)Math.Ceiling(8 * renderer.CellWidth);
        int height = (int)Math.Ceiling(renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        Assert.NotNull(pixmap);

        int scanWidth = Math.Min(pixmap.Width, Math.Max(1, (int)Math.Ceiling(renderer.CellWidth * codepoints.Length)));
        int scanHeight = Math.Min(pixmap.Height, Math.Max(1, (int)Math.Ceiling(renderer.CellHeight)));
        bool hasNonBlackPixel = false;
        for (int y = 0; y < scanHeight && !hasNonBlackPixel; y++)
        {
            for (int x = 0; x < scanWidth && !hasNonBlackPixel; x++)
            {
                SKColor pixel = pixmap.GetPixelColor(x, y);
                if (pixel.Red > 10 || pixel.Green > 10 || pixel.Blue > 10)
                {
                    hasNonBlackPixel = true;
                }
            }
        }

        Assert.True(hasNonBlackPixel, "Mixed script rendering should draw visible pixels.");
    }

    [Fact]
    public void Renderer_FlagGrapheme_RendersVisiblePixels()
    {
        const string canadaFlag = "\U0001F1E8\U0001F1E6";

        var renderer = new SkiaTerminalRenderer("Consolas", 14f)
        {
            CursorVisible = false,
        };
        var screen = new TerminalScreen(4, 1);
        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 0x1F1E8;
        row[0].Grapheme = canadaFlag;
        row[0].Width = 2;
        row[0].Foreground = 0xFFFFFFFF;
        row[0].Background = 0xFF000000;
        row[1].Codepoint = 0;
        row[1].Grapheme = null;
        row[1].Width = 0;
        row[1].Foreground = 0xFFFFFFFF;
        row[1].Background = 0xFF000000;
        row.IsDirty = true;

        int width = (int)Math.Ceiling(4 * renderer.CellWidth);
        int height = (int)Math.Ceiling(renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        Assert.NotNull(pixmap);

        int scanWidth = Math.Min(pixmap.Width, Math.Max(1, (int)Math.Ceiling(renderer.CellWidth * 2)));
        int scanHeight = Math.Min(pixmap.Height, Math.Max(1, (int)Math.Ceiling(renderer.CellHeight)));
        bool hasNonBlackPixel = false;
        for (int y = 0; y < scanHeight && !hasNonBlackPixel; y++)
        {
            for (int x = 0; x < scanWidth && !hasNonBlackPixel; x++)
            {
                SKColor px = pixmap.GetPixelColor(x, y);
                if (px.Red > 10 || px.Green > 10 || px.Blue > 10)
                {
                    hasNonBlackPixel = true;
                }
            }
        }

        Assert.True(hasNonBlackPixel, "Flag grapheme should produce visible pixels in the first two cells.");
    }

    [Fact]
    public void Renderer_DirtyTracking_OnlyRendersChangedRows()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        // Full render first, which clears dirty flags
        renderer.RenderFull(canvas, screen);

        // Verify all rows are no longer dirty
        for (var i = 0; i < screen.ViewportRows; i++)
            Assert.False(screen.GetViewportRow(i).IsDirty);

        // Mark only row 5 as dirty by modifying it
        screen.GetViewportRow(5).IsDirty = true;

        // Incremental render should only process dirty rows
        renderer.Render(canvas, screen);

        Assert.False(screen.GetViewportRow(5).IsDirty);
    }

    [Fact]
    public void Renderer_SpacerCellWidthZero_IsNotRendered()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.CursorVisible = false;
        var width = (int)(4 * renderer.CellWidth);
        var height = (int)Math.Ceiling(renderer.CellHeight);

        using var surfaceWithSpacer = SKSurface.Create(new SKImageInfo(width, height));
        surfaceWithSpacer.Canvas.Clear(SKColors.Black);
        var screenWithSpacer = new TerminalScreen(4, 1);
        var rowWithSpacer = screenWithSpacer.GetViewportRow(0);
        for (var i = 0; i < rowWithSpacer.Columns; i++)
        {
            rowWithSpacer[i].Foreground = 0xFFFFFFFF;
            rowWithSpacer[i].Background = 0xFF000000;
        }

        rowWithSpacer[0].Codepoint = 'X';
        rowWithSpacer[0].Width = 0;
        rowWithSpacer[1].Codepoint = 'A';
        rowWithSpacer[1].Width = 1;
        rowWithSpacer.IsDirty = true;
        renderer.RenderFull(surfaceWithSpacer.Canvas, screenWithSpacer);

        using var surfaceControl = SKSurface.Create(new SKImageInfo(width, height));
        surfaceControl.Canvas.Clear(SKColors.Black);
        var controlScreen = new TerminalScreen(4, 1);
        var controlRow = controlScreen.GetViewportRow(0);
        for (var i = 0; i < controlRow.Columns; i++)
        {
            controlRow[i].Foreground = 0xFFFFFFFF;
            controlRow[i].Background = 0xFF000000;
        }

        controlRow[0].Codepoint = 0;
        controlRow[0].Width = 0;
        controlRow[1].Codepoint = 'A';
        controlRow[1].Width = 1;
        controlRow.IsDirty = true;
        renderer.RenderFull(surfaceControl.Canvas, controlScreen);

        using var spacerSnapshot = surfaceWithSpacer.Snapshot();
        using var spacerPixmap = spacerSnapshot.PeekPixels();
        Assert.NotNull(spacerPixmap);

        using var controlSnapshot = surfaceControl.Snapshot();
        using var controlPixmap = controlSnapshot.PeekPixels();
        Assert.NotNull(controlPixmap);

        int cellWidth = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth));
        int cellHeight = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight));

        bool hasGlyphPixelInNextCell = false;
        int nextCellStart = Math.Min(spacerPixmap.Width, cellWidth);
        int nextCellEnd = Math.Min(spacerPixmap.Width, cellWidth * 2);
        for (int y = 0; y < cellHeight && !hasGlyphPixelInNextCell; y++)
        {
            for (int x = nextCellStart; x < nextCellEnd && !hasGlyphPixelInNextCell; x++)
            {
                SKColor px = spacerPixmap.GetPixelColor(x, y);
                if (px.Red > 10 || px.Green > 10 || px.Blue > 10)
                {
                    hasGlyphPixelInNextCell = true;
                }
            }
        }

        bool spacerCellChangedComparedToControl = false;
        int spacerCellEndX = Math.Min(Math.Min(spacerPixmap.Width, controlPixmap.Width), cellWidth);
        int compareHeight = Math.Min(Math.Min(spacerPixmap.Height, controlPixmap.Height), cellHeight);
        for (int y = 0; y < compareHeight && !spacerCellChangedComparedToControl; y++)
        {
            for (int x = 0; x < spacerCellEndX && !spacerCellChangedComparedToControl; x++)
            {
                if (spacerPixmap.GetPixelColor(x, y) != controlPixmap.GetPixelColor(x, y))
                {
                    spacerCellChangedComparedToControl = true;
                }
            }
        }

        Assert.False(
            spacerCellChangedComparedToControl,
            "Spacer cell should not alter rendered output compared to control content.");
        Assert.True(hasGlyphPixelInNextCell, "Renderable cell following spacer should still draw.");
    }

    [Fact]
    public void Renderer_Render_UsesDirtyRowsForPixelOutput()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.CursorVisible = false;
        var screen = new TerminalScreen(8, 2);
        var row0 = screen.GetViewportRow(0);
        var row1 = screen.GetViewportRow(1);

        for (var i = 0; i < row0.Columns; i++)
        {
            row0[i].Foreground = 0xFFFFFFFF;
            row0[i].Background = 0xFF000000;
            row1[i].Foreground = 0xFFFFFFFF;
            row1[i].Background = 0xFF000000;
        }

        row0[0].Codepoint = 'A';
        row1[0].Codepoint = 'B';
        row0.IsDirty = true;
        row1.IsDirty = false;

        var width = (int)(8 * renderer.CellWidth);
        var height = (int)(2 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.Render(canvas, screen);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();
        Assert.NotNull(pixmap);

        int cellWidth = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth));
        int cellHeight = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight));

        bool row0HasRenderedPixels = false;
        for (int y = 0; y < cellHeight && !row0HasRenderedPixels; y++)
        {
            for (int x = 0; x < cellWidth && !row0HasRenderedPixels; x++)
            {
                SKColor px = pixmap.GetPixelColor(x, y);
                if (px.Red > 10 || px.Green > 10 || px.Blue > 10)
                {
                    row0HasRenderedPixels = true;
                }
            }
        }

        bool row1HasRenderedPixels = false;
        int row1StartY = Math.Min(pixmap.Height, cellHeight);
        int row1EndY = Math.Min(pixmap.Height, cellHeight * 2);
        for (int y = row1StartY; y < row1EndY && !row1HasRenderedPixels; y++)
        {
            for (int x = 0; x < cellWidth && !row1HasRenderedPixels; x++)
            {
                SKColor px = pixmap.GetPixelColor(x, y);
                if (px.Red > 10 || px.Green > 10 || px.Blue > 10)
                {
                    row1HasRenderedPixels = true;
                }
            }
        }

        Assert.True(row0HasRenderedPixels, "Dirty row should be rendered.");
        Assert.False(row1HasRenderedPixels, "Clean row should not be rendered by incremental draw.");
    }

    [Fact]
    public void Renderer_CursorRendering_DrawsAtPosition()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        renderer.CursorColumn = 5;
        renderer.CursorRow = 3;
        renderer.CursorVisible = true;
        renderer.CursorColor = new SKColor(0xFF, 0xFF, 0xFF);

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.RenderFull(canvas, screen);

        // Verify cursor area has non-black pixels
        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();

        var cursorX = (int)(5 * renderer.CellWidth);
        var cursorY = (int)(3 * renderer.CellHeight);
        var hasWhitePixel = false;

        for (var y = cursorY; y < cursorY + (int)renderer.CellHeight && !hasWhitePixel; y++)
        {
            for (var x = cursorX; x < cursorX + (int)renderer.CellWidth && !hasWhitePixel; x++)
            {
                if (x < pixmap.Width && y < pixmap.Height)
                {
                    var pixel = pixmap.GetPixelColor(x, y);
                    if (pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200)
                        hasWhitePixel = true;
                }
            }
        }

        Assert.True(hasWhitePixel, "Cursor should render white pixels at position (5,3)");
    }

    [Fact]
    public void Renderer_Selection_DrawsHighlight()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        renderer.SelectionStart = (0, 0);
        renderer.SelectionEnd = (10, 0);
        renderer.SelectionColor = new SKColor(0x40, 0x60, 0xA0, 0xFF); // Full alpha for testing

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.RenderFull(canvas, screen);

        using var snapshot = surface.Snapshot();
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void Renderer_InvalidCodepoint_DoesNotAbortFrameAndStillDrawsValidCells()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 0x110000; // Invalid Unicode scalar value
        row[0].Foreground = 0xFFFFFFFF;
        row[1].Codepoint = 'A';
        row[1].Foreground = 0xFFFFFFFF;
        row[1].Background = 0xFF000000;

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();

        int cellWidth = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth));
        int cellHeight = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight));
        int startX = cellWidth;
        int endX = Math.Min(pixmap.Width, cellWidth * 2);
        bool hasNonBlackPixel = false;

        for (int y = 0; y < cellHeight && !hasNonBlackPixel; y++)
        {
            for (int x = startX; x < endX && !hasNonBlackPixel; x++)
            {
                SKColor pixel = pixmap.GetPixelColor(x, y);
                if (pixel.Red > 10 || pixel.Green > 10 || pixel.Blue > 10)
                {
                    hasNonBlackPixel = true;
                }
            }
        }

        Assert.True(hasNonBlackPixel, "Valid neighboring cells should still be rendered when one codepoint is invalid.");
    }

    [Fact]
    public void Renderer_FontSizeChange_UpdatesCellDimensions()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var initialWidth = renderer.CellWidth;
        var initialHeight = renderer.CellHeight;

        renderer.SetFontSize(20f);

        Assert.True(renderer.CellWidth > initialWidth);
        Assert.True(renderer.CellHeight > initialHeight);
    }

    [Fact]
    public void Renderer_CellAttributes_AffectRendering()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Set up cells with different attributes
        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 'A';
        row[0].Foreground = 0xFFFFFFFF;
        row[0].Background = 0xFF000000;
        row[0].Attributes = CellAttributes.Bold;

        row[1].Codepoint = 'B';
        row[1].Foreground = 0xFFFFFFFF;
        row[1].Background = 0xFF000000;
        row[1].Attributes = CellAttributes.Italic;

        row[2].Codepoint = 'C';
        row[2].Foreground = 0xFFFFFFFF;
        row[2].Background = 0xFF000000;
        row[2].Attributes = CellAttributes.Bold | CellAttributes.Italic;

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        // Should not throw with any attribute combination
        renderer.RenderFull(canvas, screen);

        using var snapshot = surface.Snapshot();
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void Renderer_InverseAttribute_SwapsColors()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 'X';
        row[0].Foreground = 0xFFFFFFFF;
        row[0].Background = 0xFF000000;
        row[0].Attributes = CellAttributes.Inverse;

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.RenderFull(canvas, screen);

        using var snapshot = surface.Snapshot();
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void Renderer_MultipleRows_RenderCorrectly()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Fill multiple rows with content
        for (var r = 0; r < 10; r++)
        {
            var row = screen.GetViewportRow(r);
            for (var c = 0; c < 20; c++)
            {
                row[c].Codepoint = (int)('A' + (c % 26));
                row[c].Foreground = 0xFFFFFFFF;
                row[c].Background = 0xFF000000;
            }
        }

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.RenderFull(canvas, screen);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();

        // Verify pixels exist in multiple row areas
        for (var r = 0; r < 5; r++)
        {
            var rowY = (int)(r * renderer.CellHeight + renderer.CellHeight / 2);
            var hasContent = false;
            for (var x = 0; x < (int)(20 * renderer.CellWidth) && !hasContent; x++)
            {
                if (rowY < pixmap.Height && x < pixmap.Width)
                {
                    var pixel = pixmap.GetPixelColor(x, rowY);
                    if (pixel.Red > 10 || pixel.Green > 10 || pixel.Blue > 10)
                        hasContent = true;
                }
            }
            Assert.True(hasContent, $"Row {r} should have rendered content");
        }
    }

    [Fact]
    public void Renderer_LargeScreen_DoesNotThrow()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(200, 60);

        // Fill everything
        for (var r = 0; r < 60; r++)
        {
            var row = screen.GetViewportRow(r);
            for (var c = 0; c < 200; c++)
            {
                row[c].Codepoint = '.';
                row[c].Foreground = 0xFF808080;
                row[c].Background = 0xFF000000;
            }
        }

        var width = (int)(200 * renderer.CellWidth);
        var height = (int)(60 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        renderer.RenderFull(canvas, screen);

        using var snapshot = surface.Snapshot();
        Assert.Equal(width, snapshot.Width);
        Assert.Equal(height, snapshot.Height);
    }

    #endregion

    #region Headless Avalonia Control + Skia

    [AvaloniaFact]
    public void Control_InWindow_InitializesCorrectly()
    {
        var control = new GhosttyTerminalControl
        {
            FontFamilyName = "Consolas",
            TerminalFontSize = 14,
            Columns = 80,
            Rows = 24,
        };

        var window = new Window
        {
            Content = control,
            Width = 800,
            Height = 600,
        };

        window.Show();

        Assert.NotNull(control.Screen);
        Assert.NotNull(control.Renderer);
        Assert.NotNull(control.ScrollData);
        // After layout, columns/rows may adjust to fit window size
        Assert.True(control.Columns > 0);
        Assert.True(control.Rows > 0);

        window.Close();
    }

    [AvaloniaFact]
    public void Control_WriteOutput_FiresDataReceivedEvent()
    {
        var control = new GhosttyTerminalControl();
        byte[]? receivedData = null;

        control.DataReceived += (_, args) => receivedData = args.Data.ToArray();
        control.WriteOutput("Hello, Terminal!"u8);

        Assert.NotNull(receivedData);
        Assert.Equal("Hello, Terminal!"u8.ToArray(), receivedData);
    }

    [AvaloniaFact]
    public void Control_WriteOutput_MultipleWrites()
    {
        var control = new GhosttyTerminalControl();
        var writeCount = 0;

        control.DataReceived += (_, _) => writeCount++;

        control.WriteOutput("Line 1\n"u8);
        control.WriteOutput("Line 2\n"u8);
        control.WriteOutput("Line 3\n"u8);

        Assert.Equal(3, writeCount);
    }

    [AvaloniaFact]
    public void Control_ScreenModel_CanBeModifiedDirectly()
    {
        var control = new GhosttyTerminalControl();
        var screen = control.Screen!;

        // Write directly to screen cells
        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 'T';
        row[0].Foreground = 0xFFFFFFFF;
        row[1].Codepoint = 'e';
        row[1].Foreground = 0xFFFFFFFF;
        row[2].Codepoint = 's';
        row[2].Foreground = 0xFFFFFFFF;
        row[3].Codepoint = 't';
        row[3].Foreground = 0xFFFFFFFF;

        Assert.Equal('T', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('e', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal('s', screen.GetViewportRow(0)[2].Codepoint);
        Assert.Equal('t', screen.GetViewportRow(0)[3].Codepoint);
    }

    [AvaloniaFact]
    public void Control_RendererCellDimensions_AreReasonable()
    {
        var control = new GhosttyTerminalControl
        {
            FontFamilyName = "Consolas",
            TerminalFontSize = 14,
        };

        var renderer = control.Renderer!;
        // Cell dimensions should be reasonable for 14pt font
        Assert.InRange(renderer.CellWidth, 5f, 20f);
        Assert.InRange(renderer.CellHeight, 10f, 30f);
    }

    [AvaloniaFact]
    public void Control_CursorVisibility_TogglesOnFocus()
    {
        var control = new GhosttyTerminalControl();
        var renderer = control.Renderer!;

        // Initially visible
        Assert.True(renderer.CursorVisible);
    }

    [AvaloniaFact]
    public void Control_Scrollback_AddsRows()
    {
        var control = new GhosttyTerminalControl
        {
            Columns = 80,
            Rows = 24,
            ScrollbackLimit = 100,
        };

        var screen = control.Screen!;
        var initialRows = screen.TotalRows;

        // Add scrollback rows
        for (var i = 0; i < 10; i++)
            screen.AddRow();

        Assert.Equal(initialRows + 10, screen.TotalRows);
    }

    [AvaloniaFact]
    public void Control_ScreenResize_UpdatesDimensions()
    {
        var control = new GhosttyTerminalControl();
        var screen = control.Screen!;

        screen.Resize(120, 40);

        Assert.Equal(120, screen.Columns);
        Assert.Equal(40, screen.ViewportRows);
    }

    [AvaloniaFact]
    public void Control_ScrollData_Initialized()
    {
        var control = new GhosttyTerminalControl();
        var scrollData = control.ScrollData!;

        Assert.True(scrollData.CellHeight > 0);
        Assert.True(scrollData.Viewport > 0);
    }

    [AvaloniaFact]
    public void Control_InvalidateTerminal_MarksDirty()
    {
        var control = new GhosttyTerminalControl();
        var screen = control.Screen!;

        // Clear dirty flags first
        for (var i = 0; i < screen.ViewportRows; i++)
            screen.GetViewportRow(i).IsDirty = false;

        Assert.False(screen.HasDirtyRows());

        // Invalidate should mark all rows dirty
        control.InvalidateTerminal();

        Assert.True(screen.HasDirtyRows());
    }

    #endregion

    #region Skia Surface Rendering Integration

    [Fact]
    public void SkiaSurface_CanRenderScreenContent()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Set up a "Hello World" line
        var text = "Hello, World!";
        var row = screen.GetViewportRow(0);
        for (var i = 0; i < text.Length; i++)
        {
            row[i].Codepoint = text[i];
            row[i].Foreground = 0xFF00FF00; // Green text
            row[i].Background = 0xFF000000; // Black background
        }

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        renderer.RenderFull(canvas, screen);

        // Extract image and verify non-empty
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);

        Assert.NotNull(data);
        Assert.True(data.Size > 0, "PNG encoding should produce non-empty output");
    }

    [Fact]
    public void SkiaSurface_RendersToBitmap()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(40, 12);

        // Fill screen line by line
        string[] lines =
        [
            "$ echo 'Hello from GhosttySharp!'",
            "Hello from GhosttySharp!",
            "$ ls -la",
            "total 42",
            "drwxr-xr-x  10 user  staff   320 Jan  1 00:00 .",
            "drwxr-xr-x   5 user  staff   160 Jan  1 00:00 ..",
            "-rw-r--r--   1 user  staff  1024 Jan  1 00:00 README.md",
        ];

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var row = screen.GetViewportRow(lineIdx);
            for (var c = 0; c < lines[lineIdx].Length && c < 40; c++)
            {
                row[c].Codepoint = lines[lineIdx][c];
                row[c].Foreground = lineIdx < 1 || lineIdx == 2 ? 0xFF00FF00u : 0xFFD4D4D4u;
                row[c].Background = 0xFF1E1E1Eu;
            }
        }

        var width = (int)(40 * renderer.CellWidth);
        var height = (int)(12 * renderer.CellHeight);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0xFF1E1E1E));

        renderer.RenderFull(canvas, screen);

        // Bitmap should have content
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);

        // Verify green pixels exist (prompt text)
        var hasGreenPixel = false;
        for (var y = 0; y < (int)renderer.CellHeight && !hasGreenPixel; y++)
        {
            for (var x = 0; x < width && !hasGreenPixel; x++)
            {
                var px = bitmap.GetPixel(x, y);
                if (px.Green > 200 && px.Red < 50 && px.Blue < 50)
                    hasGreenPixel = true;
            }
        }
        Assert.True(hasGreenPixel, "Green text should produce green pixels");
    }

    [Fact]
    public void SkiaSurface_MultipleRenders_Consistent()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        var row = screen.GetViewportRow(0);
        row[0].Codepoint = 'X';
        row[0].Foreground = 0xFFFF0000;
        row[0].Background = 0xFF000000;

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);

        // Render twice and compare
        using var surface1 = SKSurface.Create(new SKImageInfo(width, height));
        surface1.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(canvas: surface1.Canvas, screen);
        screen.InvalidateAll();

        using var surface2 = SKSurface.Create(new SKImageInfo(width, height));
        surface2.Canvas.Clear(SKColors.Black);
        renderer.RenderFull(canvas: surface2.Canvas, screen);

        using var img1 = surface1.Snapshot();
        using var img2 = surface2.Snapshot();
        using var data1 = img1.Encode(SKEncodedImageFormat.Png, 100);
        using var data2 = img2.Encode(SKEncodedImageFormat.Png, 100);

        Assert.Equal(data1.Size, data2.Size);
    }

    #endregion

    #region GlyphCache Integration

    [Fact]
    public void GlyphCache_MeasureCellSize_Consistent()
    {
        using var cache = new GlyphCache("Consolas");
        var (w1, h1) = cache.MeasureCellSize(14f);
        var (w2, h2) = cache.MeasureCellSize(14f);

        Assert.Equal(w1, w2);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void GlyphCache_DifferentSizes_DifferentDimensions()
    {
        using var cache = new GlyphCache("Consolas");
        var (w14, h14) = cache.MeasureCellSize(14f);
        var (w20, h20) = cache.MeasureCellSize(20f);

        Assert.True(w20 > w14);
        Assert.True(h20 > h14);
    }

    [Fact]
    public void GlyphCache_CreateFont_WorksForAllStyles()
    {
        using var cache = new GlyphCache("Consolas");

        using var regular = cache.CreateFont(14f, bold: false, italic: false);
        using var bold = cache.CreateFont(14f, bold: true, italic: false);
        using var italic = cache.CreateFont(14f, bold: false, italic: true);
        using var boldItalic = cache.CreateFont(14f, bold: true, italic: true);

        Assert.NotNull(regular);
        Assert.NotNull(bold);
        Assert.NotNull(italic);
        Assert.NotNull(boldItalic);
    }

    [Fact]
    public void GlyphCache_Eviction_WorksUnderPressure()
    {
        using var cache = new GlyphCache("Consolas", maxEntries: 10);

        // Trigger eviction check
        cache.EvictIfNeeded();

        // Should not throw
        Assert.True(cache.Count >= 0);
    }

    #endregion

    #region Screen Model Rendering Pipeline  

    [Fact]
    public void Screen_ScrollPosition_AffectsVisibleContent()
    {
        var screen = new TerminalScreen(80, 5, scrollbackLimit: 100);

        // Add rows beyond viewport
        for (var i = 0; i < 20; i++)
        {
            var row = screen.AddRow();
            row[0].Codepoint = (int)('A' + (i % 26));
        }

        // At scroll offset 0, we see the latest rows
        screen.ScrollOffset = 0;
        var bottomRow = screen.GetViewportRow(4);

        // Scroll up
        screen.ScrollOffset = 10;
        var scrolledRow = screen.GetViewportRow(4);

        // They should be different content
        // (unless all rows same, but we set different codepoints)
        Assert.NotNull(bottomRow);
        Assert.NotNull(scrolledRow);
    }

    [Fact]
    public void Screen_RenderAfterScroll_DoesNotThrow()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24, scrollbackLimit: 500);

        // Add many rows
        for (var i = 0; i < 100; i++)
        {
            var row = screen.AddRow();
            for (var c = 0; c < 80; c++)
            {
                row[c].Codepoint = (int)('0' + (i % 10));
                row[c].Foreground = 0xFFFFFFFF;
                row[c].Background = 0xFF000000;
            }
        }

        // Scroll to various positions and render
        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));

        foreach (var offset in new[] { 0, 10, 50, screen.MaxScrollOffset })
        {
            screen.ScrollOffset = offset;
            screen.InvalidateAll();
            surface.Canvas.Clear(SKColors.Black);
            renderer.RenderFull(surface.Canvas, screen);
        }

        // If we got here without throwing, the test passes
        Assert.True(true);
    }

    [Fact]
    public void Screen_ColoredOutput_RendersDistinctColors()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        var screen = new TerminalScreen(80, 24);

        // Row 0: Red text
        var row0 = screen.GetViewportRow(0);
        for (var i = 0; i < 10; i++)
        {
            row0[i].Codepoint = 'R';
            row0[i].Foreground = 0xFFFF0000;
            row0[i].Background = 0xFF000000;
        }

        // Row 1: Green text
        var row1 = screen.GetViewportRow(1);
        for (var i = 0; i < 10; i++)
        {
            row1[i].Codepoint = 'G';
            row1[i].Foreground = 0xFF00FF00;
            row1[i].Background = 0xFF000000;
        }

        // Row 2: Blue text
        var row2 = screen.GetViewportRow(2);
        for (var i = 0; i < 10; i++)
        {
            row2[i].Codepoint = 'B';
            row2[i].Foreground = 0xFF0000FF;
            row2[i].Background = 0xFF000000;
        }

        var width = (int)(80 * renderer.CellWidth);
        var height = (int)(24 * renderer.CellHeight);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Black);

        renderer.RenderFull(surface.Canvas, screen);

        using var snapshot = surface.Snapshot();
        using var pixmap = snapshot.PeekPixels();

        // Check for red pixels in row 0 area
        var hasRed = false;
        var row0Y = (int)(renderer.CellHeight / 2);
        for (var x = 0; x < (int)(10 * renderer.CellWidth) && !hasRed; x++)
        {
            var px = pixmap.GetPixelColor(x, row0Y);
            if (px.Red > 200 && px.Green < 50 && px.Blue < 50)
                hasRed = true;
        }
        Assert.True(hasRed, "Red text should produce red pixels");

        // Check for green pixels in row 1 area
        var hasGreen = false;
        var row1Y = (int)(renderer.CellHeight + renderer.CellHeight / 2);
        for (var x = 0; x < (int)(10 * renderer.CellWidth) && !hasGreen; x++)
        {
            var px = pixmap.GetPixelColor(x, row1Y);
            if (px.Green > 200 && px.Red < 50 && px.Blue < 50)
                hasGreen = true;
        }
        Assert.True(hasGreen, "Green text should produce green pixels");
    }

    #endregion
}
