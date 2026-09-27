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
public sealed class TerminalTypefaceCollection
{
    private readonly SKTypeface[][] _faces;

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

    /// <summary>Returns the first configured face for a style, or the first regular face if that style is empty.</summary>
    public SKTypeface GetPrimaryTypeface(TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular)
    {
        ValidateStyle(style);
        SKTypeface[] faces = _faces[(int)style];
        return faces.Length == 0 ? _faces[0][0] : faces[0];
    }

    internal ReadOnlySpan<SKTypeface> GetFaces(TerminalTypefaceStyle style) => _faces[(int)style];

    internal static void ValidateStyle(TerminalTypefaceStyle style)
    {
        if ((uint)style > (uint)TerminalTypefaceStyle.BoldItalic)
            throw new ArgumentOutOfRangeException(nameof(style));
    }
}
