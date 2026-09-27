// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

internal interface IConfiguredFontFamilyMatcher
{
    // Returns a face from this family only, or null for an unavailable family.
    // The caller releases its reference via Dispose; Skia may protect shared
    // system-font wrappers and retain their native resources in its own cache.
    SKTypeface? Match(string family, TerminalTypefaceStyle style, string? styleName = null);
}

internal sealed class SkiaConfiguredFontFamilyMatcher(SKFontManager manager) : IConfiguredFontFamilyMatcher
{
    public SKTypeface? Match(string family, TerminalTypefaceStyle style, string? styleName = null)
    {
        // MatchFamily can silently substitute the system default for an unknown
        // family. A style set is empty for an unknown family, preserving priority
        // of the next explicitly configured family (and localized family names).
        using SKFontStyleSet styles = manager.GetFontStyles(family);
        if (styles.Count == 0) return null;
        if (styleName is not null)
        {
            // An advertised name overrides the logical bold/italic category.
            // Do not silently substitute the closest weight for a missing name.
            for (int i = 0; i < styles.Count; i++)
                if (string.Equals(styles.GetStyleName(i), styleName, StringComparison.OrdinalIgnoreCase))
                    return styles.CreateTypeface(i);
            return null;
        }
        SKTypeface? candidate = styles.CreateTypeface(style switch
        {
            TerminalTypefaceStyle.Bold => SKFontStyle.Bold,
            TerminalTypefaceStyle.Italic => SKFontStyle.Italic,
            TerminalTypefaceStyle.BoldItalic => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal,
        });
        if (candidate is null || MatchesStyle(candidate, style)) return candidate;
        candidate.Dispose();
        return null;
    }

    internal static bool MatchesStyle(SKTypeface face, TerminalTypefaceStyle style)
        => style == TerminalTypefaceStyle.Regular ||
            (face.IsBold == (style is TerminalTypefaceStyle.Bold or TerminalTypefaceStyle.BoldItalic) &&
             face.IsItalic == (style is TerminalTypefaceStyle.Italic or TerminalTypefaceStyle.BoldItalic));
}

/// <summary>Owns configured face references independently of renderer/discovery caches.</summary>
internal sealed class ConfiguredFontFamilies : IDisposable
{
    private readonly HashSet<SKTypeface> _owned = new(ReferenceEqualityComparer.Instance);
    private GlyphCache? _legacy;
    private bool _disposed;
    internal TerminalTypefaceCollection Collection { get; private set; } = null!;

    internal static ConfiguredFontFamilies Load(TerminalFontFamilySettings settings,
        IConfiguredFontFamilyMatcher matcher, Func<GlyphCache> legacyFactory, string? primarySystemFamily = null)
    {
        ConfiguredFontFamilies owner = new();
        try
        {
            settings = settings.Normalize();
            List<TerminalTypefaceEntry> entries = [];
            AddFamilies(settings.Regular, TerminalTypefaceStyle.Regular, settings.RegularStyle);
            if (entries.Count == 0 && settings.Regular.IsDefaultOrEmpty && primarySystemFamily is not null &&
                Named(settings.RegularStyle) is { } primaryStyle &&
                matcher.Match(primarySystemFamily, TerminalTypefaceStyle.Regular, primaryStyle) is { } namedPrimary)
            {
                owner._owned.Add(namedPrimary);
                entries.Add(new(namedPrimary, TerminalTypefaceStyle.Regular));
            }
            int regularCount = entries.Count;
            if (regularCount == 0)
            {
                owner._legacy = legacyFactory();
                entries.Add(new(owner._legacy.RegularTypeface, TerminalTypefaceStyle.Regular));
                regularCount = 1;
            }

            AddStyle(settings.Bold, TerminalTypefaceStyle.Bold, settings.BoldStyle);
            AddStyle(settings.Italic, TerminalTypefaceStyle.Italic, settings.ItalicStyle);
            AddStyle(settings.BoldItalic, TerminalTypefaceStyle.BoldItalic, settings.BoldItalicStyle);
            owner.Collection = new(entries.ToArray());
            owner.Collection = owner.Collection.WithSyntheticStyles(settings.SyntheticBold,
                settings.SyntheticItalic, settings.SyntheticBoldItalic);
            if (settings.BoldStyle == "false" || settings.ItalicStyle == "false" || settings.BoldItalicStyle == "false")
                owner.Collection = owner.Collection.WithDisabledStyles(settings.BoldStyle == "false",
                    settings.ItalicStyle == "false", settings.BoldItalicStyle == "false");
            if (!settings.CodepointMaps.IsDefaultOrEmpty)
            {
                if (!TerminalFontCodepointMap.TryParse(settings.CodepointMaps.AsSpan(), out var mappings, out string? error))
                    throw new FormatException(error);
                TerminalTypefaceCodepointMapping[] configured = new TerminalTypefaceCodepointMapping[mappings.Length];
                for (int i = 0; i < configured.Length; i++)
                    configured[i] = new(mappings[i].First, mappings[i].Last, mappings[i].FamilyName);
                owner.Collection = owner.Collection.WithCodepointMappings(configured);
            }
            return owner;

            void AddFamilies(ImmutableArray<string> names, TerminalTypefaceStyle style, string styleName)
            {
                foreach (string family in names)
                    if (matcher.Match(family, style, Named(styleName)) is { } face)
                    {
                        owner._owned.Add(face);
                        entries.Add(new(face, style));
                    }
            }

            void AddStyle(ImmutableArray<string> names, TerminalTypefaceStyle style, string styleName)
            {
                // Disabled styles resolve to regular before discovery. Avoid
                // creating resources that no text/cursor/IME request can use.
                if (styleName == "false") return;
                int before = entries.Count;
                AddFamilies(names, style, styleName);
                if (entries.Count != before) return;
                // Ghostty inherits styled variants from the regular families
                // when no explicit styled family could be loaded.
                if (owner._legacy is { } legacy)
                {
                    if (primarySystemFamily is not null && Named(styleName) is { } namedStyle)
                    {
                        SKTypeface? named = matcher.Match(legacy.RegularTypeface.FamilyName, style, namedStyle);
                        if (named is not null) owner._owned.Add(named);
                        if (named is not null) entries.Add(new(named, style));
                        return;
                    }
                    SKTypeface candidate = legacy.GetTypeface(style is TerminalTypefaceStyle.Bold or TerminalTypefaceStyle.BoldItalic,
                        style is TerminalTypefaceStyle.Italic or TerminalTypefaceStyle.BoldItalic);
                    if (SkiaConfiguredFontFamilyMatcher.MatchesStyle(candidate, style)) entries.Add(new(candidate, style));
                    return;
                }
                for (int index = 0; index < regularCount; index++)
                    if (matcher.Match(entries[index].Typeface.FamilyName, style, Named(styleName)) is { } face)
                    {
                        owner._owned.Add(face);
                        entries.Add(new(face, style));
                    }
                // Complete missing styles only after loading all real variants.
            }

            static string? Named(string value) => value.Length == 0 || value == "false" ? null : value;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (SKTypeface face in _owned) face.Dispose();
        _owned.Clear();
        _legacy?.Dispose();
    }
}
