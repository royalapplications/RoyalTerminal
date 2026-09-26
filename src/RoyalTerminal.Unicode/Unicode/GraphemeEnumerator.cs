// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;

namespace RoyalTerminal.Unicode;

/// <summary>Enumerates extended grapheme clusters according to Unicode 18 UAX #29.</summary>
public ref struct GraphemeEnumerator
{
    private readonly ReadOnlySpan<char> _text;
    private int _offset;

    /// <summary>Creates a grapheme enumerator over UTF-16 text.</summary>
    public GraphemeEnumerator(ReadOnlySpan<char> text)
    {
        _text = text;
        _offset = 0;
    }

    /// <summary>Gets the next grapheme, or returns false at the end of the text.</summary>
    public bool MoveNext(out Grapheme grapheme)
    {
        if (_offset >= _text.Length)
        {
            grapheme = default;
            return false;
        }

        int start = _offset;
        Codepoint first = Codepoint.ReadAt(_text, _offset, out int length);
        uint previous = first.Value;
        _offset += length;
        GraphemeBreakState state = default;
        while (_offset < _text.Length)
        {
            Codepoint current = Codepoint.ReadAt(_text, _offset, out length);
            if (state.IsBreak(previous, current.Value)) break;
            previous = current.Value;
            _offset += length;
        }

        grapheme = new Grapheme(first, start, _offset - start);
        return true;
    }
}
