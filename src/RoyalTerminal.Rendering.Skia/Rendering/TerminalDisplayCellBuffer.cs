// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Reusable display-only cell projection. Font selection sees the original
/// selectors; every downstream text builder sees the same projected text and
/// unchanged grid widths. No screen, snapshot or IME source cell is mutated.
/// </summary>
internal sealed class TerminalDisplayCellBuffer : IDisposable
{
    internal const int MaxCachedCharacters = 65536;
    internal const int MaxCachedEntries = 1024;
    private readonly Dictionary<string, string> _selectorCache = new(StringComparer.Ordinal);
    private TerminalCell[] _cells = [];
    private TerminalFontResolution[] _fonts = [];
    private int _count;
    internal int CachedCharacters { get; private set; }
    internal int CachedEntries => _selectorCache.Count;
    internal ReadOnlySpan<TerminalCell> Cells => _cells.AsSpan(0, _count);
    internal ReadOnlySpan<TerminalFontResolution> Fonts => _fonts.AsSpan(0, _count);

    internal void Add(in TerminalCell source, TerminalFontResolution font)
        => AddProjected(Project(in source, font), font);

    internal void AddProjected(in TerminalCell display, TerminalFontResolution font)
    {
        if (_count == _cells.Length)
        {
            int capacity = Math.Max(8, checked(_count * 2));
            Array.Resize(ref _cells, capacity);
            Array.Resize(ref _fonts, capacity);
        }
        _cells[_count] = display;
        _fonts[_count++] = font;
    }

    internal TerminalCell Project(in TerminalCell source, TerminalFontResolution font)
    {
        TerminalCell display = source;
        if (font.ReplacementCodepoint != 0)
        {
            display.Codepoint = font.ReplacementCodepoint;
            display.Grapheme = null;
        }
        else if (source.Grapheme is { Length: > 0 } text)
        {
            display.Grapheme = WithoutSuffixSelectors(text);
            if (!ReferenceEquals(text, display.Grapheme) &&
                Rune.DecodeFromUtf16(display.Grapheme, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done &&
                consumed == display.Grapheme.Length)
            {
                display.Codepoint = rune.Value;
                display.Grapheme = null;
            }
        }
        return display;
    }

    private string WithoutSuffixSelectors(string text)
    {
        // Ghostty drops selectors in the grapheme suffix, not a base scalar
        // which happens to be a selector. ZWJ and other shaping controls stay.
        Rune.DecodeFromUtf16(text, out _, out int baseLength);
        ReadOnlySpan<char> suffix = text.AsSpan(baseLength);
        if (suffix.IndexOfAny('\uFE0E', '\uFE0F') < 0) return text;
        if (_selectorCache.TryGetValue(text, out string? cached)) return cached;
        int removed = 0;
        foreach (char value in suffix)
            if (value is '\uFE0E' or '\uFE0F') removed++;
        string result = string.Create(text.Length - removed, (text, baseLength), static (target, state) =>
        {
            int written = 0;
            for (int i = 0; i < state.text.Length; i++)
                if (i < state.baseLength || state.text[i] is not ('\uFE0E' or '\uFE0F'))
                    target[written++] = state.text[i];
        });
        long characters = (long)text.Length + result.Length;
        if (characters <= MaxCachedCharacters)
        {
            if (_selectorCache.Count >= MaxCachedEntries || CachedCharacters + characters > MaxCachedCharacters)
            {
                _selectorCache.Clear();
                CachedCharacters = 0;
            }
            _selectorCache.Add(text, result);
            CachedCharacters += (int)characters;
        }
        return result;
    }

    internal void Clear()
    {
        Array.Clear(_cells, 0, _count);
        Array.Clear(_fonts, 0, _count);
        _count = 0;
    }

    public void Dispose()
    {
        Clear();
        _cells = [];
        _fonts = [];
        _selectorCache.Clear();
        CachedCharacters = 0;
    }
}
