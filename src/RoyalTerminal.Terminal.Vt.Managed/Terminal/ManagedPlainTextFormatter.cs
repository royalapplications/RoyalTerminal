// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Plain export with Ghostty's deferred blank-cell/row semantics.</summary>
internal static class ManagedPlainTextFormatter
{
    internal static string Format(TerminalScreen screen, in TerminalSnapshotExportOptions options)
    {
        if (screen.Columns <= 0 || screen.TotalRows <= 0) return string.Empty;
        int firstRow = 0, lastRow = screen.TotalRows - 1;
        int firstColumn = 0, lastColumn = screen.Columns - 1;
        bool rectangle = false;
        if (options.Selection is TerminalSelectionRange selection)
        {
            if (screen.ViewportRows <= 0) return string.Empty;
            selection = selection.Normalize();
            int top = Math.Max(0, screen.TotalRows - screen.ViewportRows - screen.ScrollOffset);
            firstRow = top + Math.Clamp(selection.StartRow, 0, screen.ViewportRows - 1);
            lastRow = top + Math.Clamp(selection.EndRow, 0, screen.ViewportRows - 1);
            firstColumn = Math.Clamp(selection.StartColumn, 0, screen.Columns - 1);
            lastColumn = Math.Clamp(selection.EndColumn, 0, screen.Columns - 1);
            rectangle = selection.Rectangle;
            if (rectangle && firstColumn > lastColumn) (firstColumn, lastColumn) = (lastColumn, firstColumn);
        }

        bool unwrap = options.Unwrap && !rectangle;
        // A right-edge spacer belongs to the wide glyph on the following row.
        if (unwrap && lastRow + 1 < screen.TotalRows &&
            screen.GetRow(lastRow).ReadOnlyCells[lastColumn].IsWideSpacerHead)
        {
            lastRow++;
            lastColumn = 0;
        }

        StringBuilder result = new();
        int blankRows = 0, blankCells = 0;
        Span<char> scalar = stackalloc char[2];
        for (int index = firstRow; index <= lastRow; index++)
        {
            TerminalRow row = screen.GetRow(index);
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
            int start = rectangle || index == firstRow ? firstColumn : 0;
            int end = rectangle || index == lastRow ? lastColumn : cells.Length - 1;
            if (start > end) continue;
            if (start > 0)
            {
                if (cells[start].IsWideSpacerHead) continue;
                if (cells[start].Width == 0) start--;
            }

            bool hasText = false;
            for (int column = start; column <= end; column++)
            {
                if (cells[column].Width != 0 && cells[column].HasContent) { hasText = true; break; }
            }
            if (!hasText) { blankRows++; continue; }

            result.Append('\n', blankRows);
            blankRows = !unwrap || !row.WrapsToNext ? 1 : 0;
            if (!unwrap || !row.IsWrapContinuation) blankCells = 0;
            for (int column = start; column <= end; column++)
            {
                ref readonly TerminalCell cell = ref cells[column];
                if (cell.Width == 0 || cell.IsWideSpacerHead) continue;
                if (!cell.HasContent || (options.TrimTrailingWhitespace && cell.Codepoint == ' '))
                {
                    blankCells++;
                    continue;
                }

                result.Append(' ', blankCells);
                blankCells = 0;
                if (!string.IsNullOrEmpty(cell.Grapheme)) result.Append(cell.Grapheme);
                else if (Rune.TryCreate(cell.Codepoint, out Rune rune))
                    result.Append(scalar[..rune.EncodeToUtf16(scalar)]);
            }
        }

        // Blank cells without later text and trailing empty rows are always
        // omitted, even when explicit trailing ASCII spaces are retained.
        return result.ToString();
    }
}
