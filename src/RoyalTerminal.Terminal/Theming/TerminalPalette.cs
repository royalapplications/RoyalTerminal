// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Immutable 256-color terminal palette.
/// </summary>
public sealed class TerminalPalette
{
    private const int PaletteSize = 256;
    private readonly uint[] _colors;
    private readonly bool[] _explicit;

    /// <summary>
    /// Initializes a palette from an exact 256-entry color array.
    /// </summary>
    public TerminalPalette(uint[] colors, IEnumerable<int>? explicitOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Length != PaletteSize)
        {
            throw new ArgumentException("Palette must contain exactly 256 colors.", nameof(colors));
        }

        _colors = colors.ToArray();
        _explicit = new bool[PaletteSize];

        if (explicitOverrides is not null)
        {
            foreach (int index in explicitOverrides)
            {
                if ((uint)index < PaletteSize)
                {
                    _explicit[index] = true;
                }
            }
        }
    }

    /// <summary>
    /// Gets palette entry by index in [0,255].
    /// </summary>
    public uint this[int index]
    {
        get
        {
            if ((uint)index >= PaletteSize)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _colors[index];
        }
    }

    /// <summary>
    /// Returns true if the entry was explicitly overridden.
    /// </summary>
    public bool IsExplicitOverride(int index)
    {
        if ((uint)index >= PaletteSize)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _explicit[index];
    }

    /// <summary>
    /// Gets explicit override indexes.
    /// </summary>
    public IReadOnlyList<int> ExplicitOverrideIndexes
    {
        get
        {
            List<int> result = new();
            for (int i = 0; i < PaletteSize; i++)
            {
                if (_explicit[i])
                {
                    result.Add(i);
                }
            }

            return new ReadOnlyCollection<int>(result);
        }
    }

    /// <summary>
    /// Returns a copy of all palette entries.
    /// </summary>
    public uint[] ToArray() => _colors.ToArray();

    /// <summary>
    /// Returns a new palette with one entry updated.
    /// </summary>
    public TerminalPalette WithColor(int index, uint color, bool explicitOverride = true)
    {
        if ((uint)index >= PaletteSize)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        uint[] nextColors = _colors.ToArray();
        bool[] nextExplicit = _explicit.ToArray();
        nextColors[index] = color;
        if (explicitOverride)
        {
            nextExplicit[index] = true;
        }

        return new TerminalPalette(nextColors, EnumerateExplicit(nextExplicit));
    }

    /// <summary>
    /// Creates a palette from base ANSI colors and a generation mode.
    /// </summary>
    public static TerminalPalette FromBase16(
        ReadOnlySpan<uint> base16,
        TerminalPaletteGenerationMode generationMode,
        IEnumerable<int>? explicitOverrides = null)
    {
        uint[] colors = TerminalPaletteGenerator.Generate(base16, generationMode);
        HashSet<int> explicitSet = explicitOverrides is null
            ? new()
            : new(explicitOverrides.Where(static i => (uint)i < PaletteSize));
        return new TerminalPalette(colors, explicitSet);
    }

    /// <summary>
    /// Canonical xterm-style default palette.
    /// </summary>
    public static TerminalPalette CreateDefaultCanonical()
    {
        uint[] colors = TerminalPaletteGenerator.Generate(
            TerminalThemeDefaults.Base16Canonical.AsSpan(),
            TerminalPaletteGenerationMode.Canonical);
        return new TerminalPalette(colors);
    }

    private static IEnumerable<int> EnumerateExplicit(bool[] explicitMap)
    {
        for (int i = 0; i < explicitMap.Length; i++)
        {
            if (explicitMap[i])
            {
                yield return i;
            }
        }
    }
}
