// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Projects absolute selection spans into a viewport without history-sized scratch.</summary>
public static class TerminalSelectionProjection
{
    /// <summary>
    /// Returns only visible spans, with viewport-relative rows and unchanged
    /// columns/kinds/order. Multiple spans on a row and unsorted input are supported.
    /// Allocates one exact-sized result, or none when nothing is visible.
    /// </summary>
    /// <param name="spans">Absolute buffer selection spans.</param>
    /// <param name="topRow">Nonnegative absolute viewport top.</param>
    /// <param name="rows">Nonnegative viewport height.</param>
    /// <returns>A newly owned result, or the shared empty array.</returns>
    public static TerminalHighlightSpan[] ProjectViewport(ReadOnlySpan<TerminalHighlightSpan> spans, int topRow, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topRow);
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        if (rows == 0 || spans.IsEmpty) return Array.Empty<TerminalHighlightSpan>();
        int count = 0;
        foreach (ref readonly TerminalHighlightSpan span in spans)
            if (IsVisible(span.Row, topRow, rows)) count++;
        if (count == 0) return Array.Empty<TerminalHighlightSpan>();
        TerminalHighlightSpan[] result = new TerminalHighlightSpan[count];
        int index = 0;
        foreach (ref readonly TerminalHighlightSpan span in spans)
        {
            if (IsVisible(span.Row, topRow, rows))
                result[index++] = new(span.Row - topRow, span.StartColumn, span.EndColumn, span.Kind);
        }
        return result;
    }

    private static bool IsVisible(int row, int top, int rows) => (ulong)((long)row - top) < (ulong)rows;
}
