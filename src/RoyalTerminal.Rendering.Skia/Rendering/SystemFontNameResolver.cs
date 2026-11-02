// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Resolves family names and family-plus-advertised-style aliases without platform substitution.</summary>
internal sealed class SystemFontNameResolver(IConfiguredFontFamilyMatcher matcher)
{
    internal SKTypeface? Match(string family, TerminalTypefaceStyle style, string? styleName = null)
    {
        family = family.Trim();
        if (matcher.Match(family, style, styleName) is { } exact) return exact;

        // A real family always wins, including one whose name ends in "Light".
        // A missing explicit style must not reinterpret that family as an alias.
        if ((style != TerminalTypefaceStyle.Regular || styleName is not null) &&
            matcher.Match(family, TerminalTypefaceStyle.Regular) is { } existing)
        {
            existing.Dispose();
            return null;
        }

        // Accept a suffix only when the font actually advertises that style.
        // Longest family prefixes win; no hard-coded weight words are stripped.
        for (int split = family.Length - 1; split > 0; split--)
        {
            if (!char.IsWhiteSpace(family[split])) continue;
            string baseFamily = family[..split].TrimEnd();
            string advertisedStyle = family[(split + 1)..].TrimStart();
            if (baseFamily.Length == 0 || advertisedStyle.Length == 0) continue;
            SKTypeface? named = matcher.Match(baseFamily, TerminalTypefaceStyle.Regular, advertisedStyle);
            if (named is null) continue;
            // Like an explicitly configured style name, a face alias takes
            // precedence over the logical regular/bold/italic category.
            if (styleName is null) return named;
            SKTypeface? overridden = null;
            try { return overridden = matcher.Match(baseFamily, style, styleName); }
            finally { if (!ReferenceEquals(named, overridden)) named.Dispose(); }
        }
        return null;
    }

    internal SKTypeface CreateMonospaceFallback(IEnumerable<string> installedFamilies)
    {
        // Windows Terminal explicitly prefers Consolas on a family miss.
        // Other platforms may not have it; verify pitch rather than accepting
        // Skia's non-null, potentially proportional system default.
        ReadOnlySpan<string> preferred = ["Consolas", "Menlo", "Monaco", "DejaVu Sans Mono", "Liberation Mono", "Courier New", "monospace"];
        foreach (string family in preferred)
            if (TryMonospace(family) is { } face) return face;
        foreach (string family in installedFamilies)
            if (TryMonospace(family) is { } face) return face;
        return BundledTerminalFont.CreateTypeface();
    }

    private SKTypeface? TryMonospace(string family)
    {
        SKTypeface? face = matcher.Match(family, TerminalTypefaceStyle.Regular);
        if (face is null) return null;
        if (face.IsFixedPitch && !TerminalFontCoverage.IsLastResort(face))
        {
            // Fixed-pitch emoji/symbol fonts are not usable primary terminal
            // fonts even though their pitch flag is set.
            using SKFont font = new(face);
            if (font.ContainsGlyph(' ') && font.ContainsGlyph('0') && font.ContainsGlyph('M') && font.ContainsGlyph('W')) return face;
        }
        face.Dispose();
        return null;
    }
}
