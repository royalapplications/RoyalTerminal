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

    /// <summary>Whether all styles retain their default family selection.</summary>
    [JsonIgnore]
    public bool IsEmpty => Regular.IsDefaultOrEmpty && Bold.IsDefaultOrEmpty &&
        Italic.IsDefaultOrEmpty && BoldItalic.IsDefaultOrEmpty && CodepointMaps.IsDefaultOrEmpty;

    /// <summary>Trims entries without changing priority, and validates codepoint-map syntax.</summary>
    /// <exception cref="FormatException">A codepoint mapping is malformed.</exception>
    public TerminalFontFamilySettings Normalize()
    {
        ImmutableArray<string> regular = NormalizeNames(Regular), bold = NormalizeNames(Bold),
            italic = NormalizeNames(Italic), boldItalic = NormalizeNames(BoldItalic), maps = NormalizeNames(CodepointMaps);
        if (!TerminalFontCodepointMap.TryParse(maps.AsSpan(), out _, out string? error)) throw new FormatException(error);
        if (regular == Regular && bold == Bold && italic == Italic && boldItalic == BoldItalic && maps == CodepointMaps) return this;
        return this with { Regular = regular, Bold = bold, Italic = italic, BoldItalic = boldItalic, CodepointMaps = maps };
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
