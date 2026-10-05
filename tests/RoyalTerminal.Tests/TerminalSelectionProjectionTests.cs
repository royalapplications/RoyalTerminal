// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSelectionProjectionTests
{
    [Fact]
    public void PreservesOrderColumnsKindsAndMultipleSpansPerRow()
    {
        TerminalHighlightSpan[] spans = [Span(12, 5, 7), Span(9, 1, 2), Span(10, 0, 3), Span(10, 8, 9), Span(13, 2, 4)];
        Assert.Equal(new[] { Span(2, 5, 7), Span(0, 0, 3), Span(0, 8, 9) },
            TerminalSelectionProjection.ProjectViewport(spans, 10, 3));
        Assert.Equal(12, spans[0].Row);
    }

    [Fact]
    public void SignedRowArithmeticDoesNotWrapAtIntegerLimits()
    {
        TerminalHighlightSpan[] spans = [Span(int.MinValue), Span(-1), Span(0), Span(int.MaxValue - 1), Span(int.MaxValue)];
        Assert.Equal(new[] { Span(0), Span(1) },
            TerminalSelectionProjection.ProjectViewport(spans, int.MaxValue - 1, 2));
        Assert.Equal(new[] { Span(0), Span(int.MaxValue - 1) },
            TerminalSelectionProjection.ProjectViewport(spans, 0, int.MaxValue));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    public void RejectsInvalidViewport(int top, int rows)
        => Assert.Throws<ArgumentOutOfRangeException>(() => TerminalSelectionProjection.ProjectViewport([], top, rows));

    [Fact]
    public void EmptyAndHiddenSelectionsAllocateNothing()
    {
        TerminalHighlightSpan[] spans = [Span(1), Span(2)];
        TerminalSelectionProjection.ProjectViewport(spans, 20, 5);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var hidden = TerminalSelectionProjection.ProjectViewport(spans, 20, 5);
        var empty = TerminalSelectionProjection.ProjectViewport([], 0, 5);
        var zero = TerminalSelectionProjection.ProjectViewport(spans, 0, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(Array.Empty<TerminalHighlightSpan>(), hidden);
        Assert.Same(hidden, empty);
        Assert.Same(hidden, zero);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void LargeHistoryAllocatesOnlyVisibleResultAndDoesNotAliasInput()
    {
        TerminalHighlightSpan[] spans = new TerminalHighlightSpan[100_000];
        for (int row = 0; row < spans.Length; row++) spans[row] = Span(row);
        TerminalSelectionProjection.ProjectViewport(spans, 50_000, 24);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var visible = TerminalSelectionProjection.ProjectViewport(spans, 50_000, 24);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(24, visible.Length);
        Assert.InRange(allocated, 1, 1024);
        visible[0] = Span(1000);
        Assert.Equal(50_000, spans[50_000].Row);
        Assert.Equal(12, TerminalSelectionProjection.ProjectViewport(spans, 50_000, 12).Length);
    }

    [Fact]
    public void SeededProjectionMatchesSimpleFilterOracle()
    {
        Random random = new(14404);
        for (int sample = 0; sample < 100; sample++)
        {
            TerminalHighlightSpan[] spans = new TerminalHighlightSpan[200];
            for (int i = 0; i < spans.Length; i++) spans[i] = Span(random.Next(-100, 1000), i, i + 1);
            int top = random.Next(0, 1000), rows = random.Next(0, 100);
            List<TerminalHighlightSpan> expected = new();
            foreach (var span in spans)
                if (span.Row >= top && (long)span.Row < (long)top + rows)
                    expected.Add(new(span.Row - top, span.StartColumn, span.EndColumn, span.Kind));
            Assert.Equal(expected, TerminalSelectionProjection.ProjectViewport(spans, top, rows));
        }
    }

    private static TerminalHighlightSpan Span(int row, int start = 0, int end = 79)
        => new(row, start, end, TerminalHighlightKind.Selection);
}
