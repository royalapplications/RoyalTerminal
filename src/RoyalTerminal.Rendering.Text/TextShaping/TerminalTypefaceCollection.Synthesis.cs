// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Outline effects applied to a configured face without changing its glyph identity.</summary>
[Flags]
public enum TerminalFontSynthesis
{
    /// <summary>Use the face's own outlines.</summary>
    None = 0,
    /// <summary>Embolden the outlines.</summary>
    Bold = 1,
    /// <summary>Shear the outlines to the right.</summary>
    Italic = 2,
}

public sealed partial class TerminalTypefaceCollection
{
    // Two bits per logical style; immutable copies need no parallel metadata array.
    private readonly byte _synthesis;

    /// <summary>Outline effects for a configured style; disabled styles always use regular outlines.</summary>
    public TerminalFontSynthesis GetSynthesis(TerminalTypefaceStyle style)
        => (TerminalFontSynthesis)((_synthesis >> (2 * (int)GetEffectiveStyle(style))) & 3);

    /// <summary>
    /// Completes missing styles from the first regular face with non-color glyphs.
    /// Existing styled faces are never changed. Each toggle is independent; a
    /// disabled synthesis aliases regular outlines rather than disabling that style.
    /// The returned collection borrows the same faces and preserves mappings.
    /// </summary>
    public TerminalTypefaceCollection WithSyntheticStyles(bool bold = true, bool italic = true, bool boldItalic = true)
    {
        if (_faces[1].Length != 0 && _faces[2].Length != 0 && _faces[3].Length != 0) return this;
        SKTypeface? regular = null;
        foreach (SKTypeface face in _faces[0])
        {
            bool color = face.GetTableSize(0x73626978) > 0 || face.GetTableSize(0x434F4C52) > 0 ||
                face.GetTableSize(0x43424454) > 0 || face.GetTableSize(0x53564720) > 0;
            if (!color || face.GetGlyph('A') != 0) { regular = face; break; }
        }
        // An emoji-only collection remains usable, but cannot supply synthetic text.
        if (regular is null) return this;
        SKTypeface[][] faces = (SKTypeface[][])_faces.Clone();
        byte effects = _synthesis;
        bool realBold = _faces[1].Length != 0 && ((_synthesis >> 2) & 3) == 0;
        Complete(TerminalTypefaceStyle.Italic, regular, italic ? TerminalFontSynthesis.Italic : TerminalFontSynthesis.None);
        Complete(TerminalTypefaceStyle.Bold, regular, bold ? TerminalFontSynthesis.Bold : TerminalFontSynthesis.None);
        Complete(TerminalTypefaceStyle.BoldItalic,
            !boldItalic ? regular : realBold ? faces[1][0] : faces[2][0],
            !boldItalic ? TerminalFontSynthesis.None : realBold ? TerminalFontSynthesis.Italic :
                (TerminalFontSynthesis)((effects >> 4) & 3) | TerminalFontSynthesis.Bold);
        return new(this, faces, effects);

        void Complete(TerminalTypefaceStyle style, SKTypeface face, TerminalFontSynthesis synthesis)
        {
            int index = (int)style;
            if (faces[index].Length != 0) return;
            faces[index] = [face];
            effects = (byte)(effects | ((byte)synthesis << (index * 2)));
        }
    }

    private TerminalTypefaceCollection(TerminalTypefaceCollection source, SKTypeface[][] faces, byte synthesis)
        : this(source, source._disabledStyles)
    {
        _faces = faces;
        _synthesis = synthesis;
    }
}
