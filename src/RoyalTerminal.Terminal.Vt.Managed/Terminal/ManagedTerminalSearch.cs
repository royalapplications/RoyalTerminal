// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Literal, ASCII-case-insensitive search over published terminal cells. KMP and
/// a coordinate ring retain only needle-sized scratch, even for long wrapped lines.
/// Caller serializes access with terminal mutation/publication.
/// </summary>
internal sealed class ManagedTerminalSearch
{
    private string _needle = string.Empty;
    private int[] _failure = [];
    private CellPoint[] _points = [];
    private int _matched;
    private int _nextPoint;

    private readonly record struct CellPoint(int Row, int Column);

    internal void Populate(TerminalScreen screen, string needle, List<TerminalSearchMatch> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        SetNeedle(needle ?? string.Empty);
        if (_needle.Length == 0) return;
        _matched = 0;
        _nextPoint = 0;

        // Mirror plain, trimmed, unwrapped formatting without constructing text
        // for a row/page/buffer. Delay blanks so trailing spaces are not searchable.
        long blankCells = 0;
        int blankRows = 0;
        CellPoint lastPoint = default;
        Span<char> scalar = stackalloc char[2];
        for (int rowIndex = 0; rowIndex < screen.TotalRows; rowIndex++)
        {
            TerminalRow row = screen.GetRow(rowIndex);
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
            bool hasText = false;
            foreach (ref readonly TerminalCell cell in cells)
            {
                if (cell.Width != 0 && cell.HasContent) { hasText = true; break; }
            }

            if (!hasText)
            {
                blankRows++;
                continue;
            }

            for (int i = 0; i < blankRows; i++)
            {
                CellPoint point = i == 0 ? lastPoint : new(lastPoint.Row + i, 0);
                Feed('\n', point, destination);
            }
            blankRows = row.WrapsToNext ? 0 : 1;
            if (!row.IsWrapContinuation) blankCells = 0;

            for (int column = 0; column < cells.Length; column++)
            {
                ref readonly TerminalCell cell = ref cells[column];
                if (cell.Width == 0 || cell.IsWideSpacerHead) continue;
                if (!cell.HasContent || cell.Codepoint == ' ')
                {
                    blankCells++;
                    continue;
                }

                // The plain formatter maps deferred blanks backwards from the
                // next visible cell, including blanks in a preceding wrapped row.
                long firstBlank = (long)rowIndex * screen.Columns + column - blankCells;
                for (long offset = 0; offset < blankCells; offset++)
                {
                    long position = Math.Max(0, firstBlank + offset);
                    Feed(' ', new((int)(position / screen.Columns), (int)(position % screen.Columns)), destination);
                }
                blankCells = 0;
                lastPoint = new(rowIndex, column);
                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    foreach (char character in cell.Grapheme) Feed(character, lastPoint, destination);
                }
                else if ((uint)cell.Codepoint < 0x80)
                {
                    Feed((char)cell.Codepoint, lastPoint, destination);
                }
                else
                {
                    Rune rune = Rune.TryCreate(cell.Codepoint, out Rune valid) ? valid : Rune.ReplacementChar;
                    int length = rune.EncodeToUtf16(scalar);
                    for (int i = 0; i < length; i++) Feed(scalar[i], lastPoint, destination);
                }
            }
        }

        if (screen.TotalRows > 0 && !screen.GetRow(screen.TotalRows - 1).WrapsToNext)
            Feed('\n', lastPoint, destination);
    }

    private void SetNeedle(string needle)
    {
        if (string.Equals(_needle, needle, StringComparison.Ordinal)) return;
        _needle = needle;
        if (_failure.Length < needle.Length)
        {
            _failure = new int[needle.Length];
            _points = new CellPoint[needle.Length];
        }
        if (needle.Length == 0) return;
        _failure[0] = 0;
        for (int i = 1, prefix = 0; i < needle.Length; i++)
        {
            char character = Fold(needle[i]);
            while (prefix > 0 && Fold(needle[prefix]) != character) prefix = _failure[prefix - 1];
            if (Fold(needle[prefix]) == character) prefix++;
            _failure[i] = prefix;
        }
    }

    private void Feed(char character, CellPoint point, List<TerminalSearchMatch> destination)
    {
        character = Fold(character);
        _points[_nextPoint] = point;
        if (++_nextPoint == _needle.Length) _nextPoint = 0;
        while (_matched > 0 && Fold(_needle[_matched]) != character) _matched = _failure[_matched - 1];
        if (Fold(_needle[_matched]) == character) _matched++;
        if (_matched != _needle.Length) return;

        CellPoint start = _points[_nextPoint];
        destination.Add(new(start.Row, start.Column, point.Column) { EndAbsoluteRow = point.Row });
        // Preserve the longest suffix so overlapping matches are returned.
        _matched = _failure[_matched - 1];
    }

    private static char Fold(char value) => value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;
}
