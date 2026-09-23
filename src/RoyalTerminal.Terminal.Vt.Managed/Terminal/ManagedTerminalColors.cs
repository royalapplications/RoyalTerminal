// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

/// <summary>Separates configured colors from application-controlled OSC overrides.</summary>
internal sealed class ManagedTerminalColors(TerminalTheme configuredTheme)
{
    private TerminalTheme _configuredTheme = configuredTheme;
    private Dictionary<int, uint>? _paletteOverrides;
    private uint? _foreground;
    private uint? _background;
    private uint? _cursor;

    internal void Configure(TerminalTheme theme) => _configuredTheme = theme;

    internal void SetPalette(int index, uint color)
    {
        (_paletteOverrides ??= new())[index] = color;
    }

    internal uint GetPalette(int index) => _paletteOverrides is not null && _paletteOverrides.TryGetValue(index, out uint color)
        ? color : _configuredTheme.Palette[index];

    internal void ResetPalette(int? index)
    {
        if (index is int value)
        {
            _paletteOverrides?.Remove(value);
        }
        else
        {
            _paletteOverrides?.Clear();
        }
    }

    internal void SetDynamic(int selector, uint? color)
    {
        switch (selector)
        {
            case 10: _foreground = color; break;
            case 11: _background = color; break;
            case 12: _cursor = color; break;
        }
    }

    internal TerminalTheme GetEffectiveTheme()
    {
        if (_foreground is null && _background is null && _cursor is null &&
            (_paletteOverrides is null || _paletteOverrides.Count == 0))
        {
            // Share the immutable configured palette until an application changes it.
            return _configuredTheme;
        }

        TerminalPalette palette = _configuredTheme.Palette;
        if (_paletteOverrides is { Count: > 0 })
        {
            uint[] colors = palette.ToArray();
            HashSet<int> explicitEntries = new(palette.ExplicitOverrideIndexes);
            foreach (KeyValuePair<int, uint> entry in _paletteOverrides)
            {
                colors[entry.Key] = entry.Value;
                explicitEntries.Add(entry.Key);
            }

            palette = new TerminalPalette(colors, explicitEntries);
        }

        return new TerminalTheme(
            _foreground ?? _configuredTheme.DefaultForeground,
            _background ?? _configuredTheme.DefaultBackground,
            _cursor ?? _configuredTheme.CursorColor,
            palette,
            _configuredTheme.PaletteGenerationMode,
            _configuredTheme.OscColorReportFormat,
            _configuredTheme.SelectionForeground,
            _configuredTheme.SelectionBackground,
            _configuredTheme.BoldColor,
            _configuredTheme.CursorTextColor);
    }
}
