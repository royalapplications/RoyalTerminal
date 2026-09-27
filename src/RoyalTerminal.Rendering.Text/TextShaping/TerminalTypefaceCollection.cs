// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>The requested terminal style, independent of a face's intrinsic style.</summary>
public enum TerminalTypefaceStyle
{
    /// <summary>Normal terminal text.</summary>
    Regular,
    /// <summary>Bold terminal text.</summary>
    Bold,
    /// <summary>Italic terminal text.</summary>
    Italic,
    /// <summary>Bold italic terminal text.</summary>
    BoldItalic,
}

/// <summary>A caller-owned face configured for a logical terminal style.</summary>
/// <param name="Typeface">The face; it must remain alive while the collection is in use.</param>
/// <param name="Style">The logical style, which need not match the face's intrinsic style.</param>
public readonly record struct TerminalTypefaceEntry(SKTypeface Typeface, TerminalTypefaceStyle Style);

/// <summary>
/// Immutable, ordered configured faces. Entries are copied, but their typefaces
/// are borrowed: keep them alive until all renderers and resolvers using this
/// collection have been disposed. At least one regular face is required.
/// </summary>
public sealed partial class TerminalTypefaceCollection
{
    private readonly SKTypeface[][] _faces;
    private readonly byte _disabledStyles;

    /// <summary>Copies configured entries, preserving their order within each style.</summary>
    public TerminalTypefaceCollection(params TerminalTypefaceEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<SKTypeface>[] styles = [[], [], [], []];
        foreach (TerminalTypefaceEntry entry in entries)
        {
            ValidateStyle(entry.Style);
            ArgumentNullException.ThrowIfNull(entry.Typeface);
            ObjectDisposedException.ThrowIf(entry.Typeface.Handle == 0, entry.Typeface);
            styles[(int)entry.Style].Add(entry.Typeface);
        }
        if (styles[0].Count == 0)
            throw new ArgumentException("A regular terminal face is required.", nameof(entries));
        _faces = [styles[0].ToArray(), styles[1].ToArray(), styles[2].ToArray(), styles[3].ToArray()];
    }

    /// <summary>Returns the first configured face for a style, or the first regular face if that style is disabled or empty.</summary>
    public SKTypeface GetPrimaryTypeface(TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular)
    {
        style = GetEffectiveStyle(style);
        SKTypeface[] faces = _faces[(int)style];
        return faces.Length == 0 ? _faces[0][0] : faces[0];
    }

    internal ReadOnlySpan<SKTypeface> GetFaces(TerminalTypefaceStyle style) => _faces[(int)style];

    /// <summary>Returns regular for a disabled style; bold italic is independent of bold and italic.</summary>
    public TerminalTypefaceStyle GetEffectiveStyle(TerminalTypefaceStyle style)
    {
        ValidateStyle(style);
        return (_disabledStyles & (1 << (int)style)) == 0 ? style : TerminalTypefaceStyle.Regular;
    }

    /// <summary>Copies this borrowed collection with replacement disabled-style flags, preserving codepoint mappings.</summary>
    public TerminalTypefaceCollection WithDisabledStyles(bool bold = false, bool italic = false, bool boldItalic = false)
        => new(this, (byte)((bold ? 2 : 0) | (italic ? 4 : 0) | (boldItalic ? 8 : 0)));

    private TerminalTypefaceCollection(TerminalTypefaceCollection source, byte disabledStyles)
    {
        _faces = source._faces;
        _codepointRanges = source._codepointRanges;
        _codepointFamilies = source._codepointFamilies;
        _disabledStyles = disabledStyles;
    }

    internal static void ValidateStyle(TerminalTypefaceStyle style)
    {
        if ((uint)style > (uint)TerminalTypefaceStyle.BoldItalic)
            throw new ArgumentOutOfRangeException(nameof(style));
    }
}
