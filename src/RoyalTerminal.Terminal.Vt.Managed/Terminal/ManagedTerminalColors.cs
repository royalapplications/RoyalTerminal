// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

/// <summary>Separates configured colors from application-controlled OSC overrides.</summary>
internal sealed class ManagedTerminalColors(TerminalTheme configuredTheme)
{
    private TerminalTheme _configuredTheme = configuredTheme;
    private Dictionary<int, uint>? _paletteOverrides;
    private uint? _foreground;
    private uint? _background;
    private uint? _cursor;
    private uint? _defaultForeground = configuredTheme.DefaultForeground;
    private uint? _defaultBackground = configuredTheme.DefaultBackground;
    private uint? _defaultCursor = configuredTheme.CursorColor;

    internal void Configure(TerminalTheme theme)
    {
        _configuredTheme = theme;
        _defaultForeground = theme.DefaultForeground;
        _defaultBackground = theme.DefaultBackground;
        _defaultCursor = theme.CursorColor;
    }

    // Installation is limited to an unpublished processor. Keep the original
    // palette and sparse override identity, including overrides equal to defaults.
    internal void InstallSnapshot(GhosttySnapshotTerminalState state, TerminalTheme host)
    {
        uint[] original = new uint[256];
        Dictionary<int, uint>? overrides = null;
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = 0xFF000000 | state.OriginalPaletteColor(i);
            if (state.HasPaletteOverride(i))
                (overrides ??= new())[i] = 0xFF000000 | state.CurrentPaletteColor(i);
        }
        GhosttySnapshotTerminalHeader header = state.Header;
        TerminalTheme configured = new(
            Argb(header.Foreground.Default) ?? host.DefaultForeground,
            Argb(header.Background.Default) ?? host.DefaultBackground,
            Argb(header.CursorColor.Default) ?? host.CursorColor,
            new TerminalPalette(original), host.PaletteGenerationMode, host.OscColorReportFormat,
            host.SelectionForeground, host.SelectionBackground, host.BoldColor, host.CursorTextColor);
        // All allocations precede mutation. Missing defaults remain missing in
        // protocol/snapshot state; the host fallback is only for rendering.
        _configuredTheme = configured;
        _paletteOverrides = overrides;
        _defaultForeground = Argb(header.Foreground.Default);
        _defaultBackground = Argb(header.Background.Default);
        _defaultCursor = Argb(header.CursorColor.Default);
        _foreground = Argb(header.Foreground.Override);
        _background = Argb(header.Background.Override);
        _cursor = Argb(header.CursorColor.Override);
    }

    internal GhosttySnapshotDynamicColor GetSnapshotDynamic(int selector) => selector switch
    {
        10 => new(_defaultForeground & 0xFFFFFF, _foreground & 0xFFFFFF),
        11 => new(_defaultBackground & 0xFFFFFF, _background & 0xFFFFFF),
        12 => new(_defaultCursor & 0xFFFFFF, _cursor & 0xFFFFFF),
        _ => throw new ArgumentOutOfRangeException(nameof(selector)),
    };

    internal uint? GetDynamic(int selector) => selector switch
    {
        10 => _foreground ?? _defaultForeground,
        11 => _background ?? _defaultBackground,
        12 => _cursor ?? _defaultCursor,
        _ => throw new ArgumentOutOfRangeException(nameof(selector)),
    };

    internal uint GetOriginalPalette(int index) => _configuredTheme.Palette[index];
    internal bool HasPaletteOverride(int index) => _paletteOverrides?.ContainsKey(index) == true;

    private static uint? Argb(uint? color) => color | 0xFF000000;

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
