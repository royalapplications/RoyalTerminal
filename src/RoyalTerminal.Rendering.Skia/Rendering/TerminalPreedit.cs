// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Unicode;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Immutable UI-only composition. Offsets from Avalonia are UTF-16, not cells.</summary>
internal sealed class TerminalPreedit
{
    private readonly List<(string Text, int Width)> _cells = [];
    private readonly int _caret;
    private readonly TerminalCell[] _renderCells;

    internal TerminalPreedit(string text, int? cursor = null)
    {
        int position = Math.Clamp(cursor ?? text.Length, 0, text.Length);
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            int width = TerminalCellWidthCalculator.GetCellWidth(element);
            if (width <= 0) continue;
            if (elements.ElementIndex < position) _caret++;
            _cells.Add((element, Math.Min(width, 2)));
        }
        _renderCells = new TerminalCell[_cells.Count];
        for (int i = 0; i < _cells.Count; i++)
            _renderCells[i] = new() { Codepoint = char.ConvertToUtf32(_cells[i].Text, 0),
                Grapheme = _cells[i].Text, Width = _cells[i].Width };
    }

    internal int Count => _cells.Count;
    internal (string Text, int Width) this[int index] => _cells[index];
    internal ReadOnlySpan<TerminalCell> RenderCell(int index) => _renderCells.AsSpan(index, 1);

    // Keep the caret's cluster visible, shift left at the right edge and never
    // split a wide cluster. Unlike Ghostty's scalar overlay, retain combining
    // marks/ZWJ sequences so IME previews agree with committed text.
    internal TerminalPreeditRange Range(int start, int maximum)
    {
        if (maximum < 0 || Count == 0) return new(0, -1, 0, 0, 0);
        int capacity = maximum + 1;
        int offset = 0, end = Math.Min(_caret + 1, Count), width = 0;
        for (int i = 0; i < end; i++) width += _cells[i].Width;
        while (offset < end && width > capacity) width -= _cells[offset++].Width;
        while (end < Count && width + _cells[end].Width <= capacity) width += _cells[end++].Width;
        int column = Math.Clamp(start, 0, Math.Max(0, capacity - width));
        int caret = column;
        for (int i = offset; i < Math.Min(_caret, end); i++) caret += _cells[i].Width;
        return new(column, column + width - 1, offset, end, Math.Min(maximum, caret));
    }
}

internal readonly record struct TerminalPreeditRange(int Start, int End, int Offset, int Limit, int Caret);
