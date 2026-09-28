// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Ordered configured system families for each terminal style. Empty regular
/// settings retain the legacy primary family/file. Empty or unavailable styled
/// lists use variants of the configured regular families, then regular faces.
/// </summary>
public sealed record TerminalFontFamilySettings
{
    /// <summary>Creates default font settings with synthesis enabled for all styles.</summary>
    public TerminalFontFamilySettings() : this(true, true, true) { }

    /// <summary>Creates font settings, preserving enabled synthesis when JSON omits the optional toggles.</summary>
    [JsonConstructor]
    public TerminalFontFamilySettings(bool syntheticBold = true, bool syntheticItalic = true, bool syntheticBoldItalic = true)
    {
        SyntheticBold = syntheticBold;
        SyntheticItalic = syntheticItalic;
        SyntheticBoldItalic = syntheticBoldItalic;
    }

    /// <summary>Immutable empty configuration, preserving legacy font selection.</summary>
    public static TerminalFontFamilySettings Default { get; } = new();

    /// <summary>Regular families in decreasing priority order.</summary>
    public ImmutableArray<string> Regular { get; init; } = [];
    /// <summary>Bold families in decreasing priority order.</summary>
    public ImmutableArray<string> Bold { get; init; } = [];
    /// <summary>Italic families in decreasing priority order.</summary>
    public ImmutableArray<string> Italic { get; init; } = [];
    /// <summary>Bold-italic families in decreasing priority order.</summary>
    public ImmutableArray<string> BoldItalic { get; init; } = [];

    /// <summary>Ghostty-style codepoint range mappings; later matching entries win.</summary>
    public ImmutableArray<string> CodepointMaps { get; init; } = [];

    /// <summary>Advertised regular face name; empty/default uses automatic selection. False has no effect on regular text.</summary>
    public string RegularStyle { get; init; } = string.Empty;
    /// <summary>Advertised bold face name; false routes bold requests to regular, independently of bold italic.</summary>
    public string BoldStyle { get; init; } = string.Empty;
    /// <summary>Advertised italic face name; false routes italic requests to regular, independently of bold italic.</summary>
    public string ItalicStyle { get; init; } = string.Empty;
    /// <summary>Advertised bold-italic face name; false routes combined requests to regular.</summary>
    public string BoldItalicStyle { get; init; } = string.Empty;

    /// <summary>Synthesize missing bold outlines; does not affect real bold faces or combined styles.</summary>
    public bool SyntheticBold { get; init; } = true;
    /// <summary>Synthesize missing italic outlines; does not affect real italic faces or the combined toggle.</summary>
    public bool SyntheticItalic { get; init; } = true;
    /// <summary>Complete missing bold-italic outlines independently of the individual style toggles.</summary>
    public bool SyntheticBoldItalic { get; init; } = true;

    /// <summary>Whether no family, named/disabled style or codepoint override has been configured.</summary>
    [JsonIgnore]
    public bool IsEmpty => Regular.IsDefaultOrEmpty && Bold.IsDefaultOrEmpty &&
        Italic.IsDefaultOrEmpty && BoldItalic.IsDefaultOrEmpty && CodepointMaps.IsDefaultOrEmpty &&
        string.IsNullOrEmpty(RegularStyle) && string.IsNullOrEmpty(BoldStyle) &&
        string.IsNullOrEmpty(ItalicStyle) && string.IsNullOrEmpty(BoldItalicStyle) &&
        SyntheticBold && SyntheticItalic && SyntheticBoldItalic;

    /// <summary>Trims entries without changing priority, and validates codepoint-map syntax.</summary>
    /// <exception cref="FormatException">A codepoint mapping is malformed.</exception>
    public TerminalFontFamilySettings Normalize()
    {
        ImmutableArray<string> regular = NormalizeNames(Regular), bold = NormalizeNames(Bold),
            italic = NormalizeNames(Italic), boldItalic = NormalizeNames(BoldItalic), maps = NormalizeNames(CodepointMaps);
        string regularStyle = NormalizeStyle(RegularStyle), boldStyle = NormalizeStyle(BoldStyle),
            italicStyle = NormalizeStyle(ItalicStyle), boldItalicStyle = NormalizeStyle(BoldItalicStyle);
        if (!TerminalFontCodepointMap.TryValidate(maps.AsSpan(), out string? error)) throw new FormatException(error);
        if (regular == Regular && bold == Bold && italic == Italic && boldItalic == BoldItalic && maps == CodepointMaps &&
            regularStyle == RegularStyle && boldStyle == BoldStyle && italicStyle == ItalicStyle && boldItalicStyle == BoldItalicStyle) return this;
        return this with
        {
            Regular = regular, Bold = bold, Italic = italic, BoldItalic = boldItalic, CodepointMaps = maps,
            RegularStyle = regularStyle, BoldStyle = boldStyle, ItalicStyle = italicStyle, BoldItalicStyle = boldItalicStyle,
        };
    }

    private static string NormalizeStyle(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized == "default" ? string.Empty : normalized;
    }

    private static ImmutableArray<string> NormalizeNames(ImmutableArray<string> names)
    {
        if (names.IsDefaultOrEmpty) return [];
        bool normalized = true;
        foreach (string? name in names)
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim()) { normalized = false; break; }
        if (normalized) return names;
        ImmutableArray<string>.Builder result = ImmutableArray.CreateBuilder<string>(names.Length);
        foreach (string? name in names)
            if (!string.IsNullOrWhiteSpace(name)) result.Add(name.Trim());
        return result.ToImmutable();
    }
}
