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
    SKTypeface? Match(string family, TerminalTypefaceStyle style);
}

internal sealed class SkiaConfiguredFontFamilyMatcher(SKFontManager manager) : IConfiguredFontFamilyMatcher
{
    public SKTypeface? Match(string family, TerminalTypefaceStyle style)
    {
        // MatchFamily can silently substitute the system default for an unknown
        // family. A style set is empty for an unknown family, preserving priority
        // of the next explicitly configured family (and localized family names).
        using SKFontStyleSet styles = manager.GetFontStyles(family);
        if (styles.Count == 0) return null;
        return styles.CreateTypeface(style switch
        {
            TerminalTypefaceStyle.Bold => SKFontStyle.Bold,
            TerminalTypefaceStyle.Italic => SKFontStyle.Italic,
            TerminalTypefaceStyle.BoldItalic => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal,
        });
    }
}

/// <summary>Owns configured face references independently of renderer/discovery caches.</summary>
internal sealed class ConfiguredFontFamilies : IDisposable
{
    private readonly HashSet<SKTypeface> _owned = new(ReferenceEqualityComparer.Instance);
    private GlyphCache? _legacy;
    private bool _disposed;
    internal TerminalTypefaceCollection Collection { get; private set; } = null!;

    internal static ConfiguredFontFamilies Load(TerminalFontFamilySettings settings,
        IConfiguredFontFamilyMatcher matcher, Func<GlyphCache> legacyFactory)
    {
        ConfiguredFontFamilies owner = new();
        try
        {
            settings = settings.Normalize();
            List<TerminalTypefaceEntry> entries = [];
            AddFamilies(settings.Regular, TerminalTypefaceStyle.Regular);
            int regularCount = entries.Count;
            if (regularCount == 0)
            {
                owner._legacy = legacyFactory();
                entries.Add(new(owner._legacy.RegularTypeface, TerminalTypefaceStyle.Regular));
                regularCount = 1;
            }

            AddStyle(settings.Bold, TerminalTypefaceStyle.Bold);
            AddStyle(settings.Italic, TerminalTypefaceStyle.Italic);
            AddStyle(settings.BoldItalic, TerminalTypefaceStyle.BoldItalic);
            owner.Collection = new(entries.ToArray());
            return owner;

            void AddFamilies(ImmutableArray<string> names, TerminalTypefaceStyle style)
            {
                foreach (string family in names)
                    if (matcher.Match(family, style) is { } face)
                    {
                        owner._owned.Add(face);
                        entries.Add(new(face, style));
                    }
            }

            void AddStyle(ImmutableArray<string> names, TerminalTypefaceStyle style)
            {
                int before = entries.Count;
                AddFamilies(names, style);
                if (entries.Count != before) return;
                // Ghostty inherits styled variants from the regular families
                // when no explicit styled family could be loaded.
                if (owner._legacy is { } legacy)
                {
                    entries.Add(new(legacy.GetTypeface(style is TerminalTypefaceStyle.Bold or TerminalTypefaceStyle.BoldItalic,
                        style is TerminalTypefaceStyle.Italic or TerminalTypefaceStyle.BoldItalic), style));
                    return;
                }
                for (int index = 0; index < regularCount; index++)
                    if (matcher.Match(entries[index].Typeface.FamilyName, style) is { } face)
                    {
                        owner._owned.Add(face);
                        entries.Add(new(face, style));
                    }
                // An unavailable variant leaves the style empty; the resolver
                // then uses ordered regular faces before system discovery.
            }
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
