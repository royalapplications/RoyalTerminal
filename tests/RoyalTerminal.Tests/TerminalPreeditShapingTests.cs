// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// References: Ghostty renderer/generic.zig addPreeditCell uses isolated scalars;
// microsoft/terminal src/tsf/Implementation.cpp retains the composition string;
// xterm.js src/browser/input/CompositionHelper.ts uses a separate text DOM view.
// RoyalTerminal keeps Ghostty's cell geometry/underline and UI-only ownership,
// but retains whole-grapheme and cross-cluster shaping, including at the caret.
public sealed class TerminalPreeditShapingTests(ITestOutputHelper output)
{
    [Fact]
    public void WarmPreviewUsesOneRunAndOneCaretRedrawInsteadOfPerClusterDraws()
    {
        using SkiaTerminalRenderer renderer = CreateRenderer("JetBrainsMono-Regular.ttf");
        renderer.Preedit = new(new string('a', 80), 40);
        TerminalScreen screen = new(80, 1);
        using SKBitmap bitmap = new((int)(80 * renderer.CellWidth), (int)renderer.CellHeight);
        using SKCanvas canvas = new(bitmap);
        for (int i = 0; i < 32; i++) renderer.RenderFull(canvas, screen);
        renderer.ResetTextRenderDiagnostics();
        const int frames = 256;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < frames; i++) renderer.RenderFull(canvas, screen);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
        output.WriteLine($"{frames} warm 80-cell frames: {elapsed.TotalMilliseconds:F2} ms; " +
            $"{allocated} allocated bytes; {diagnostics.ShapedRuns} shaped draws.");
        // Timing/allocation observations are diagnostic, never flaky CI gates.
        Assert.Equal(frames * 2, diagnostics.ShapedRuns);
        Assert.Equal(0, diagnostics.FallbackRuns);
    }

    [Theory]
    [InlineData("JetBrainsMono-Regular.ttf", "===>", 0)]
    [InlineData("JetBrainsMono-Regular.ttf", "===>", 1)]
    [InlineData("JetBrainsMono-Regular.ttf", "===>", 2)]
    [InlineData("JetBrainsMono-Regular.ttf", "a\u0301bc", 2)]
    [InlineData("KawkabMono-Regular.ttf", "ببب", 0)]
    [InlineData("KawkabMono-Regular.ttf", "ببب", 1)]
    [InlineData("KawkabMono-Regular.ttf", "ببب", 2)]
    [InlineData("KawkabMono-Regular.ttf", "لا", 1)]
    public void PreviewAndCaretUseTheSameShapedGlyphsAsCommittedText(string font, string text, int cursor)
    {
        const int columns = 16;
        using SkiaTerminalRenderer renderer = CreateRenderer(font);
        TerminalScreen screen = new(columns, 1);
        BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\x1b[3G" + text));
        TerminalPreedit preedit = new(text, cursor);
        TerminalPreeditRange range = preedit.Range(2, columns - 1);
        // Apply the same overlay bounds to the reference; ordinary row text may
        // overhang adjacent empty cells, but preedit cannot paint outside its area.
        using SKBitmap committed = Render(renderer, screen, new SKRect(range.Start * renderer.CellWidth, 0,
            (range.End + 1) * renderer.CellWidth, renderer.CellHeight));

        TerminalScreen empty = new(columns, 1);
        byte[] before = new BasicVtProcessor(empty).GetBinarySnapshot();
        renderer.CursorColumn = 2;
        renderer.CursorColor = new SKColor(empty.DefaultBackground);
        renderer.CursorTextColor = new SKColor(empty.DefaultForeground);
        renderer.Preedit = preedit;
        using SKBitmap preview = Render(renderer, empty);

        AssertTextPixelsEqual(committed, preview);
        Assert.Equal(before, new BasicVtProcessor(empty).GetBinarySnapshot());
    }

    [Theory]
    [InlineData("JetBrainsMono-Regular.ttf", "===>")]
    [InlineData("KawkabMono-Regular.ttf", "بببب")]
    public void CompatibleClustersAreOneShapedRunPlusCaretRedraw(string font, string text)
    {
        using SkiaTerminalRenderer renderer = CreateRenderer(font);
        renderer.Preedit = new(text, 1);
        using SKBitmap bitmap = Render(renderer, new(16, 1));
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
        Assert.Equal(2, diagnostics.ShapedRuns);
        Assert.Equal(0, diagnostics.FallbackRuns);
    }

    [Fact]
    public void DifferentScriptsInTheSameFontDoNotShareHarfBuzzScriptGuessing()
    {
        using SkiaTerminalRenderer renderer = CreateRenderer("KawkabMono-Regular.ttf");
        renderer.Preedit = new("abببcd", 3);
        using SKBitmap bitmap = Render(renderer, new(16, 1));
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
        Assert.Equal(4, diagnostics.ShapedRuns); // Three scripts/runs + caret context.
        Assert.Equal(0, diagnostics.FallbackRuns);
    }

    [Theory]
    [InlineData("JetBrainsMono-Regular.ttf", "===>===>", 0, 0)]
    [InlineData("JetBrainsMono-Regular.ttf", "===>===>", 8, 4)]
    [InlineData("KawkabMono-Regular.ttf", "بببببببب", 0, 0)]
    [InlineData("KawkabMono-Regular.ttf", "بببببببب", 8, 4)]
    [InlineData("JetBrainsMono-Regular.ttf", "aa😀aa😀aa", 12, 5)]
    public void ClippedPreviewRetainsOffscreenShapingContext(string font, string text, int cursor, int offset)
    {
        using SkiaTerminalRenderer renderer = CreateRenderer(font);
        TerminalScreen fullScreen = new(16, 1);
        renderer.CursorColor = new SKColor(fullScreen.DefaultBackground);
        renderer.CursorTextColor = new SKColor(fullScreen.DefaultForeground);
        renderer.Preedit = new(text, cursor);
        using SKBitmap full = Render(renderer, fullScreen);
        using SKBitmap clipped = Render(renderer, new(4, 1));
        Assert.Equal(offset, renderer.Preedit.Range(0, 3).Offset);
        int columns = 0;
        for (int i = 0; i < offset; i++) columns += renderer.Preedit.RenderCells[i].Width;
        AssertTextPixelsEqual(full, clipped, columns * (int)renderer.CellWidth);
    }

    [Fact]
    public void FontFallbackRunsKeepWideAndCombiningClustersWhole()
    {
        using SkiaTerminalRenderer renderer = CreateRenderer("KawkabMono-Regular.ttf");
        renderer.Preedit = new("بب😀a\u0301😀بب", 5);
        using SKBitmap bitmap = Render(renderer, new(16, 1));
        TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
        Assert.True(diagnostics.FallbackFontHits > 0);
        Assert.True(diagnostics.ShapedRuns >= 5);
        Assert.Equal(0, diagnostics.FallbackRuns);
        ReadOnlySpan<TerminalCell> cells = renderer.Preedit.RenderCells;
        Assert.Equal("😀", cells[2].Grapheme);
        Assert.Equal(2, cells[2].Width);
        Assert.Equal("a\u0301", cells[3].Grapheme);
        Assert.Equal(1, cells[3].Width);
    }

    private static SkiaTerminalRenderer CreateRenderer(string font)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", font);
        Assert.True(File.Exists(path), $"Missing required font fixture: {path}");
        SkiaTerminalRenderer renderer = new("fixture", 20, TerminalFontSource.File, path)
        {
            EnableLigatures = true,
            EnableTextRenderDiagnostics = true,
            CursorVisible = false,
        };
        // Integer translations make cropped and full bitmaps directly comparable.
        renderer.SetCellSize(MathF.Ceiling(renderer.CellWidth), MathF.Ceiling(renderer.CellHeight));
        return renderer;
    }

    private static SKBitmap Render(SkiaTerminalRenderer renderer, TerminalScreen screen, SKRect? clip = null)
    {
        SKBitmap bitmap = new((int)(screen.Columns * renderer.CellWidth), (int)renderer.CellHeight);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(new SKColor(screen.DefaultBackground));
        if (clip.HasValue) canvas.ClipRect(clip.Value);
        renderer.RenderFull(canvas, screen);
        return bitmap;
    }

    private static void AssertTextPixelsEqual(SKBitmap expected, SKBitmap actual, int expectedOffset = 0)
    {
        Assert.Equal(expected.Height, actual.Height);
        int ink = 0;
        int differences = 0;
        string? firstDifference = null;
        // The underline is an overlay decoration, not part of the glyph shape.
        for (int y = 0; y < actual.Height - 2; y++)
        for (int x = 0; x < actual.Width; x++)
        {
            SKColor expectedPixel = expected.GetPixel(x + expectedOffset, y);
            SKColor actualPixel = actual.GetPixel(x, y);
            if (expectedPixel != actualPixel)
            {
                differences++;
                firstDifference ??= $"({x}, {y}): expected {expectedPixel}, actual {actualPixel}";
            }
            if (expectedPixel.Red > 80) ink++;
        }
        Assert.True(differences == 0, $"{differences} text pixels differ; first {firstDifference}.");
        Assert.True(ink > 0, "The comparison must contain rendered glyphs, not just empty backgrounds.");
    }
}
