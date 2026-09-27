// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalDisplayRenderingTests
{
    public static IEnumerable<object[]> RenderingCases()
    {
        for (int pipeline = 0; pipeline < 4; pipeline++)
            foreach (byte width in new byte[] { 1, 2 })
                foreach (bool cursor in new[] { false, true })
                    yield return [pipeline, width, cursor];
    }

    [Theory]
    [MemberData(nameof(RenderingCases))]
    public void MissingClusterMatchesOneReplacementAcrossTextPathsAndCursor(int pipeline, byte width, bool cursor)
    {
        using SkiaTerminalRenderer renderer = CreateRenderer();
        renderer.EnableTextShaping = pipeline != 0;
        if (pipeline == 2) renderer.SetCellSize(renderer.CellWidth * 4, renderer.CellHeight);
        if (pipeline == 3)
        {
            renderer.TextRenderPipeline = TerminalTextRenderPipeline.Pretext;
            renderer.EnableLigatures = false;
        }
        renderer.CursorVisible = cursor;
        renderer.CursorColumn = 1;
        renderer.CursorStyle = CursorStyle.Block;
        TerminalScreen source = CreateScreen("a\u0301\U0010FFFE", width);
        TerminalScreen expected = CreateScreen(null, width);
        expected.GetViewportRow(0).Cells[1].Codepoint = 0xFFFD;
        TerminalCell[] before = source.GetViewportRow(0).ReadOnlyCells.ToArray();
        using SKBitmap actualPixels = Render(renderer, source);
        using SKBitmap expectedPixels = Render(renderer, expected);
        Assert.Equal(expectedPixels.Bytes, actualPixels.Bytes);
        Assert.Equal(before, source.GetViewportRow(0).ReadOnlyCells.ToArray());
        Assert.Contains(actualPixels.Pixels, color => color != new SKColor(source.DefaultBackground));
        if (pipeline == 3 && renderer.IsPretextTextRenderPipelineAvailable)
        {
            TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics();
            Assert.True(diagnostics.PretextRuns > 0);
            Assert.Equal(0, diagnostics.ShapedRuns);
            Assert.Equal(0, diagnostics.PretextFallbackRuns);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedScalarAndSelectorTextKeepNeighborPositions(bool shaping)
    {
        using SkiaTerminalRenderer renderer = CreateRenderer();
        renderer.EnableTextShaping = shaping;
        TerminalScreen source = CreateScreen("A\uFE0E", 1);
        source.GetViewportRow(0).Cells[2].Codepoint = 0x10FFFE;
        TerminalScreen expected = CreateScreen(null, 1);
        expected.GetViewportRow(0).Cells[1].Codepoint = 'A';
        expected.GetViewportRow(0).Cells[2].Codepoint = 0xFFFD;
        using SKBitmap actualPixels = Render(renderer, source);
        using SKBitmap expectedPixels = Render(renderer, expected);
        Assert.Equal(expectedPixels.Bytes, actualPixels.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreeditAndCaretProjectWholeClustersWithoutMutatingComposition(bool caretAtEnd)
    {
        const string source = "A\uFE0E\U0010FFFE\u0301B";
        const string expected = "A\uFFFDB";
        using SkiaTerminalRenderer renderer = CreateRenderer();
        renderer.CursorColumn = 4;
        TerminalScreen screen = new(6, 1);
        byte[] before = new BasicVtProcessor(screen).GetBinarySnapshot();
        TerminalPreedit preedit = new(source, caretAtEnd ? source.Length : 0);
        TerminalCell[] composition = preedit.RenderCells.ToArray();
        renderer.Preedit = preedit;
        using SKBitmap actualPixels = Render(renderer, screen);
        renderer.Preedit = new(expected, caretAtEnd ? expected.Length : 0);
        using SKBitmap expectedPixels = Render(renderer, screen);
        Assert.Equal(expectedPixels.Bytes, actualPixels.Bytes);
        Assert.Equal(composition, preedit.RenderCells.ToArray());
        Assert.Equal(before, new BasicVtProcessor(screen).GetBinarySnapshot());
    }

    [Fact]
    public void SameBaseWithDifferentCoverageCannotReuseWrongTextCacheEntry()
    {
        using SkiaTerminalRenderer renderer = CreateRenderer();
        TerminalScreen complete = CreateScreen("a\u0301", 1);
        TerminalScreen missing = CreateScreen("a\u0301\U0010FFFE", 1);
        using SKBitmap before = Render(renderer, complete);
        using SKBitmap replaced = Render(renderer, missing);
        using SKBitmap after = Render(renderer, complete);
        Assert.Equal(before.Bytes, after.Bytes);
        Assert.False(before.Bytes.AsSpan().SequenceEqual(replaced.Bytes));
    }

    private static SkiaTerminalRenderer CreateRenderer()
    {
        string font = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "JetBrainsMono-Regular.ttf");
        SkiaTerminalRenderer renderer = new(new TerminalFontResolver(new TerminalDisplayCellTests.MissingMatcher()), font, 20)
        { CursorVisible = false, EnableTextRenderDiagnostics = true, EnableLigatures = true };
        renderer.SetCellSize(MathF.Ceiling(renderer.CellWidth), MathF.Ceiling(renderer.CellHeight));
        return renderer;
    }

    private static TerminalScreen CreateScreen(string? grapheme, byte width)
    {
        TerminalScreen screen = new(6, 1);
        Span<TerminalCell> cells = screen.GetViewportRow(0).Cells;
        cells[1] = new() { Codepoint = 'a', Grapheme = grapheme, Width = width,
            Foreground = screen.DefaultForeground, Background = screen.DefaultBackground,
            Attributes = CellAttributes.Bold | CellAttributes.Italic };
        if (width == 2) cells[2].Width = 0;
        cells[1 + width] = new() { Codepoint = 'B', Width = 1,
            Foreground = screen.DefaultForeground, Background = screen.DefaultBackground };
        return screen;
    }

    private static SKBitmap Render(SkiaTerminalRenderer renderer, TerminalScreen screen)
    {
        SKBitmap bitmap = new((int)MathF.Ceiling(screen.Columns * renderer.CellWidth), (int)MathF.Ceiling(renderer.CellHeight));
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(new SKColor(screen.DefaultBackground));
        renderer.RenderFull(canvas, screen);
        return bitmap;
    }
}
