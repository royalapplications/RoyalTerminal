// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal font fallback resolver.

using System.Collections.Generic;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Threading;
using RoyalTerminal.Unicode;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Result of resolving a typeface and display text for a codepoint or grapheme.
/// </summary>
public readonly record struct TerminalFontResolution(SKTypeface Typeface, bool UsedFallback)
{
    /// <summary>Outline effects for the selected configured face; discovery and mapped faces have none.</summary>
    public TerminalFontSynthesis Synthesis { get; init; }

    /// <summary>
    /// Gets the scalar to display instead of the entire unsupported input cluster:
    /// U+FFFD, or a space when no replacement glyph is available. Zero preserves
    /// the input. This is a display decision; never replace stored terminal text.
    /// </summary>
    public int ReplacementCodepoint { get; init; }
}

/// <summary>
/// Resolves primary/fallback typefaces for terminal text rendering.
/// </summary>
public sealed partial class TerminalFontResolver : IDisposable
{
    private const int VariationSelector15 = 0xFE0E;
    private const int VariationSelector16 = 0xFE0F;
    private static readonly string[] s_emojiOnlyLanguageTags = ["und-Zsye"];

    private readonly SKFontManager? _fontManager;
    private readonly ITerminalFontMatcher _fontMatcher;
    private readonly bool _ownsFontManager;
    private readonly string? _preferredEmojiFamily;
    private SKTypeface? _preferredEmojiTypeface;
    private bool _ownsPreferredEmojiTypeface = true;
    private bool _preferredEmojiResolved;
    private readonly Dictionary<FontFallbackCacheKey, FontFallbackCacheEntry> _fallbackCache = new();
    private readonly HashSet<SKTypeface> _retainedTypefaces = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<nint, SKFont> _containsGlyphFontCache = new();
    private readonly Dictionary<nint, TerminalGlyphPresentation> _presentationCache = new();
    private readonly object _sync = new();
    private int _disposeState;

    /// <summary>
    /// Gets the number of cached fallback entries.
    /// </summary>
    public int CachedFallbackCount
    {
        get
        {
            lock (_sync)
            {
                return _fallbackCache.Count;
            }
        }
    }

    /// <summary>Creates a resolver using the supplied, caller-owned font manager or an owned default manager.</summary>
    public TerminalFontResolver(SKFontManager? fontManager = null)
    {
        _fontManager = fontManager ?? SKFontManager.CreateDefault();
        _ownsFontManager = fontManager is null;
        _fontMatcher = new SkiaTerminalFontMatcher(_fontManager);
        _preferredEmojiFamily = OperatingSystem.IsMacOS() ? SkiaTerminalFontMatcher.AppleColorEmojiFamily : null;
    }

    internal TerminalFontResolver(ITerminalFontMatcher fontMatcher, string? preferredEmojiFamily = null)
    {
        ArgumentNullException.ThrowIfNull(fontMatcher);
        _fontMatcher = fontMatcher;
        _preferredEmojiFamily = preferredEmojiFamily;
    }

    /// <summary>
    /// Resolves a typeface for a single Unicode codepoint.
    /// </summary>
    /// <exception cref="InvalidOperationException">No available face can render the input, U+FFFD or a space.</exception>
    public TerminalFontResolution ResolveTypeface(
        SKTypeface primaryTypeface,
        int codepoint,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(primaryTypeface);
        ThrowIfDisposed();

        if (!Rune.IsValid(codepoint))
        {
            return new TerminalFontResolution(primaryTypeface, UsedFallback: false);
        }

        return TryResolveTypefaceCore(primaryTypeface, codepoint, culture, null, out TerminalFontResolution result)
            ? result
            : ResolveReplacement(primaryTypeface, culture, null);
    }

    /// <summary>
    /// Resolves a typeface for a UTF-16 text segment.
    /// </summary>
    /// <exception cref="InvalidOperationException">No available face can render the input cluster, U+FFFD or a space.</exception>
    public TerminalFontResolution ResolveTypeface(
        SKTypeface primaryTypeface,
        ReadOnlySpan<char> text,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(primaryTypeface);
        ThrowIfDisposed();
        return ResolveText(primaryTypeface, text, culture);
    }

    private TerminalFontResolution ResolveText(SKTypeface primaryTypeface, ReadOnlySpan<char> text,
        CultureInfo? culture, CollectionState? collection = null, TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular)
    {
        if (text.IsEmpty)
        {
            return new TerminalFontResolution(primaryTypeface, UsedFallback: false)
            { Synthesis = collection?.Configured.GetSynthesis(style) ?? TerminalFontSynthesis.None };
        }
        if (Rune.DecodeFromUtf16(text, out Rune firstRune, out int charsConsumed) != OperationStatus.Done)
            return ResolveReplacement(primaryTypeface, culture, null, collection, style);

        // Ghostty only accepts a selector immediately after the base scalar.
        // A later selector, keycap, modifier or tag cannot change that request.
        bool? explicitEmojiPresentation = GetExplicitPresentation(text[charsConsumed..]);

        if (!TryResolveTypefaceCore(primaryTypeface, firstRune.Value, culture, explicitEmojiPresentation,
            out TerminalFontResolution primary, collection, style))
            return ResolveReplacement(primaryTypeface, culture, explicitEmojiPresentation, collection, style);
        if (charsConsumed == text.Length || ContainsGrapheme(primary.Typeface, text, explicitEmojiPresentation))
        {
            return primary;
        }

        // Ghostty's run iterator discovers candidates lazily: first the base
        // character's font, then each substantive component's font. The cache
        // stores candidates per codepoint, never the final cluster decision.
        // Rechecking the complete span avoids first-rune cache collisions
        // without allocating a string key or a temporary candidate collection.
        ReadOnlySpan<char> remaining = text[charsConsumed..];
        while (!remaining.IsEmpty &&
               Rune.DecodeFromUtf16(remaining, out Rune component, out int consumed) == OperationStatus.Done)
        {
            remaining = remaining[consumed..];
            if (IsGraphemePresentationControl(component.Value))
            {
                continue;
            }

            // Discover components with their own Unicode default, not the
            // base's explicit presentation. Whole-cluster validation below
            // permits either presentation for these additional components.
            if (!TryResolveTypefaceCore(primaryTypeface, component.Value, culture, null,
                out TerminalFontResolution candidate, collection, style)) break;
            if (candidate.Typeface.Handle != primary.Typeface.Handle &&
                ContainsGrapheme(candidate.Typeface, text, explicitEmojiPresentation))
            {
                return candidate;
            }
        }

        return ResolveReplacement(primaryTypeface, culture, explicitEmojiPresentation, collection, style);
    }

    /// <summary>
    /// Resolves a typeface for a UTF-16 string segment.
    /// </summary>
    /// <exception cref="InvalidOperationException">No available face can render the input cluster, U+FFFD or a space.</exception>
    public TerminalFontResolution ResolveTypeface(
        SKTypeface primaryTypeface,
        string text,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ResolveTypeface(primaryTypeface, text.AsSpan(), culture);
    }

    private bool TryResolveTypefaceCore(
        SKTypeface primaryTypeface,
        int codepoint,
        CultureInfo? culture,
        bool? explicitEmojiPresentation,
        out TerminalFontResolution resolution,
        CollectionState? collection = null,
        TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular)
    {
        if (collection is not null)
            return TryResolveCollectionFace(collection, style, primaryTypeface, codepoint, culture, explicitEmojiPresentation, out resolution);
        // A configured font is authoritative without an explicit selector,
        // including a deliberately chosen monochrome emoji font.
        if (ContainsGlyph(primaryTypeface, codepoint, explicitEmojiPresentation))
        {
            resolution = new TerminalFontResolution(primaryTypeface, UsedFallback: false);
            return true;
        }

        bool preferEmojiPresentation = explicitEmojiPresentation ?? new Codepoint((uint)codepoint).IsEmojiPresentation;
        resolution = ResolveCachedFallback(primaryTypeface, codepoint, culture, preferEmojiPresentation);
        // Ghostty's final collection lookup accepts any presentation in an
        // already configured face; a discovered wrong-presentation face is not
        // admitted to the collection by that last-resort lookup.
        return resolution.UsedFallback || ContainsGlyph(primaryTypeface, codepoint);
    }

    private TerminalFontResolution ResolveReplacement(SKTypeface primaryTypeface, CultureInfo? culture, bool? presentation,
        CollectionState? collection = null, TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular)
    {
        // RunIterator replaces the whole unsupported cell, never just the
        // missing component. Preserve the original cell width in the renderer.
        if (TryResolveTypefaceCore(primaryTypeface, 0xFFFD, culture, presentation, out TerminalFontResolution replacement, collection, style))
            return replacement with { ReplacementCodepoint = 0xFFFD };
        if (TryResolveTypefaceCore(primaryTypeface, ' ', culture, presentation, out replacement, collection, style))
            return replacement with { ReplacementCodepoint = ' ' };
        throw new InvalidOperationException("No available terminal font can render a replacement character or space.");
    }

    private TerminalFontResolution ResolveCachedFallback(
        SKTypeface primaryTypeface,
        int codepoint,
        CultureInfo? culture,
        bool preferEmojiPresentation,
        SKFontStyle? discoveryStyle = null)
    {
        CultureInfo usedCulture = culture ?? CultureInfo.CurrentUICulture;
        string cultureName = usedCulture.Name;

        FontFallbackCacheKey key = new(
            primaryTypeface.Handle,
            discoveryStyle?.Weight ?? primaryTypeface.FontWeight,
            discoveryStyle?.Width ?? primaryTypeface.FontWidth,
            discoveryStyle?.Slant ?? primaryTypeface.FontSlant,
            codepoint,
            cultureName,
            preferEmojiPresentation);

        FontFallbackCacheEntry entry;
        lock (_sync)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(TerminalFontResolver));
            }

            if (!_fallbackCache.TryGetValue(key, out entry))
            {
                entry = CreateFallbackEntry(
                    primaryTypeface,
                    codepoint,
                    usedCulture,
                    preferEmojiPresentation,
                    discoveryStyle);
                _fallbackCache.Add(key, entry);
                if (entry.FallbackTypeface is { } retained) _retainedTypefaces.Add(retained);
            }
        }

        if (entry.FallbackTypeface is null)
        {
            return new TerminalFontResolution(primaryTypeface, UsedFallback: false);
        }

        return new TerminalFontResolution(entry.FallbackTypeface, UsedFallback: true);
    }

    /// <summary>
    /// Disposes cached fallback typefaces and owned font manager.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        lock (_sync)
        {
            HashSet<SKTypeface> disposedTypefaces = new(ReferenceEqualityComparer.Instance);

            foreach (SKFont font in _containsGlyphFontCache.Values)
            {
                font.Dispose();
            }

            _containsGlyphFontCache.Clear();

            foreach (TerminalGlyphPresentation presentation in _presentationCache.Values) presentation.Dispose();
            _presentationCache.Clear();

            foreach (FontFallbackCacheEntry entry in _fallbackCache.Values)
            {
                if (!entry.Owned || entry.FallbackTypeface is not { } fallbackTypeface || _borrowedTypefaces.Contains(fallbackTypeface))
                {
                    continue;
                }

                if (!disposedTypefaces.Add(fallbackTypeface))
                {
                    continue;
                }

                fallbackTypeface.Dispose();
            }

            _fallbackCache.Clear();
            foreach (CollectionState collection in _collections.Values)
                foreach (SKTypeface? face in collection.Descriptors.Values)
                    if (face is not null && !_borrowedTypefaces.Contains(face) && disposedTypefaces.Add(face)) face.Dispose();
            _collections.Clear();
            if (_ownsPreferredEmojiTypeface && _preferredEmojiTypeface is { } preferred &&
                !_borrowedTypefaces.Contains(preferred) && disposedTypefaces.Add(preferred)) preferred.Dispose();
            _borrowedTypefaces.Clear();
            _preferredEmojiTypeface = null;
            _retainedTypefaces.Clear();
        }

        if (_ownsFontManager)
        {
            _fontManager?.Dispose();
        }
    }

    private FontFallbackCacheEntry CreateFallbackEntry(
        SKTypeface primaryTypeface,
        int codepoint,
        CultureInfo culture,
        bool preferEmojiPresentation,
        SKFontStyle? discoveryStyle)
    {
        // Ghostty explicitly prefers the system Apple emoji family on macOS.
        // Resolve it once, in its regular style, and verify the actual family:
        // a missing named font must not silently turn into a system substitute.
        // This method is called under _sync, including the negative cache.
        if (preferEmojiPresentation && _preferredEmojiFamily is not null &&
            _fontMatcher is ITerminalFontFamilyMatcher familyMatcher)
        {
            if (!_preferredEmojiResolved)
            {
                SKTypeface? candidate = familyMatcher.MatchFamily(_preferredEmojiFamily);
                if (ReferenceEquals(candidate, primaryTypeface))
                {
                    // A matcher can return the caller's object. Never retain
                    // or dispose it as an owned fallback; the per-primary
                    // result cache still avoids repeating this query.
                    if (string.Equals(primaryTypeface.FamilyName, _preferredEmojiFamily, StringComparison.Ordinal) &&
                        ContainsGlyph(primaryTypeface, codepoint, preferEmojiPresentation)) return FontFallbackCacheEntry.NoFallback;
                    _preferredEmojiResolved = true;
                }
                else
                {
                    if (candidate is not null && string.Equals(candidate.FamilyName, _preferredEmojiFamily, StringComparison.Ordinal))
                    {
                        _preferredEmojiTypeface = candidate;
                        _retainedTypefaces.Add(candidate);
                        _ownsPreferredEmojiTypeface = !_borrowedTypefaces.Contains(candidate);
                    }
                    else if (candidate is not null) ReleaseRejectedTypeface(candidate, primaryTypeface);
                    _preferredEmojiResolved = true;
                }
            }

            if (_preferredEmojiTypeface is { } preferred && ContainsGlyph(preferred, codepoint, preferEmojiPresentation))
                return preferred.Handle == primaryTypeface.Handle ? FontFallbackCacheEntry.NoFallback : new(preferred, _ownsPreferredEmojiTypeface);
        }

        string[]? languageTags = GetLanguageTags(culture, preferEmojiPresentation);
        string? familyName = preferEmojiPresentation
            ? null
            : string.IsNullOrWhiteSpace(primaryTypeface.FamilyName)
                ? null
                : primaryTypeface.FamilyName;
        FontFallbackCacheEntry entry = Match(familyName);
        // A file-backed family may not be installed in the system collection.
        // Do not let a failed family-specific match suppress global discovery.
        if (entry.FallbackTypeface is null && familyName is not null) entry = Match(null);
        if (entry.FallbackTypeface is not null || _fontMatcher is not ITerminalFontCandidateMatcher candidates) return entry;

        // A cmap match may have the wrong presentation. Ghostty keeps iterating
        // its platform discovery descriptors instead of accepting that failure
        // as proof that no suitable face exists. The result cache above keeps
        // this cold path out of repeated rendering, including complete misses.
        foreach (SKTypeface candidate in candidates.MatchCandidates(discoveryStyle ?? primaryTypeface.FontStyle, languageTags, codepoint,
            rejected => ReleaseRejectedTypeface(rejected, primaryTypeface)))
        {
            entry = Accept(candidate);
            if (entry.FallbackTypeface is not null) return entry;
        }
        return FontFallbackCacheEntry.NoFallback;

        FontFallbackCacheEntry Match(string? family)
        {
            SKTypeface? fallbackTypeface = _fontMatcher.MatchCharacter(
                family, discoveryStyle ?? primaryTypeface.FontStyle, languageTags, codepoint);
            return Accept(fallbackTypeface);
        }

        FontFallbackCacheEntry Accept(SKTypeface? fallbackTypeface)
        {
            if (fallbackTypeface is null || ReferenceEquals(fallbackTypeface, primaryTypeface))
                return FontFallbackCacheEntry.NoFallback;
            bool accepted = false;
            try
            {
                if (fallbackTypeface.Handle != primaryTypeface.Handle && !TerminalFontCoverage.IsLastResort(fallbackTypeface) &&
                    ContainsGlyph(fallbackTypeface, codepoint, preferEmojiPresentation))
                {
                    accepted = true;
                    return new FontFallbackCacheEntry(fallbackTypeface, !_borrowedTypefaces.Contains(fallbackTypeface));
                }
                return FontFallbackCacheEntry.NoFallback;
            }
            finally { if (!accepted) ReleaseRejectedTypeface(fallbackTypeface, primaryTypeface); }
        }
    }

    private void ReleaseRejectedTypeface(SKTypeface fallbackTypeface, SKTypeface primaryTypeface)
    {
        if (ReferenceEquals(fallbackTypeface, primaryTypeface) || _borrowedTypefaces.Contains(fallbackTypeface)) return;

        // System matchers can return an object already retained for a
        // different presentation/codepoint. Reject this request, not the
        // lifetime of the earlier successful cache entry.
        // Discovery can reject hundreds of faces. Membership must not scan
        // every cached scalar or mapped descriptor for every rejected face.
        if (_retainedTypefaces.Contains(fallbackTypeface)) return;

        // Remove the font holding a rejected candidate before releasing it;
        // native handles can be reused by the subsequent global match.
        if (_containsGlyphFontCache.Remove(fallbackTypeface.Handle, out SKFont? font)) font.Dispose();
        if (_presentationCache.Remove(fallbackTypeface.Handle, out TerminalGlyphPresentation? presentation)) presentation.Dispose();
        fallbackTypeface.Dispose();
    }

    private bool ContainsGlyph(SKTypeface typeface, int codepoint, bool? emojiPresentation = null)
    {
        lock (_sync)
        {
            ushort glyph = GetContainsGlyphFont(typeface).GetGlyph(codepoint);
            if (glyph == 0) return false;
            if (emojiPresentation is null) return true;
            if (!_presentationCache.TryGetValue(typeface.Handle, out TerminalGlyphPresentation? presentation))
            {
                presentation = new TerminalGlyphPresentation(typeface);
                _presentationCache.Add(typeface.Handle, presentation);
            }
            return presentation.IsColorGlyph(glyph) == emojiPresentation.Value;
        }
    }

    private bool ContainsGrapheme(SKTypeface typeface, ReadOnlySpan<char> text, bool? explicitEmojiPresentation)
    {
        bool first = true;
        while (!text.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(text, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                return false;
            }

            if (!IsGraphemePresentationControl(rune.Value) &&
                !ContainsGlyph(typeface, rune.Value, first ? explicitEmojiPresentation : null))
            {
                return false;
            }

            text = text[consumed..];
            first = false;
        }

        return true;
    }

    private static bool IsGraphemePresentationControl(int codepoint)
        => codepoint is VariationSelector15 or VariationSelector16 or 0x200D;

    private SKFont GetContainsGlyphFont(SKTypeface typeface)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(TerminalFontResolver));
            }

            nint typefaceHandle = typeface.Handle;
            if (_containsGlyphFontCache.TryGetValue(typefaceHandle, out SKFont? font))
            {
                return font;
            }

            font = new SKFont(typeface);
            _containsGlyphFontCache.Add(typefaceHandle, font);
            return font;
        }
    }

    private static string[]? GetLanguageTags(CultureInfo culture, bool preferEmojiPresentation)
    {
        if (preferEmojiPresentation)
        {
            if (string.IsNullOrWhiteSpace(culture.Name))
            {
                return s_emojiOnlyLanguageTags;
            }

            return ["und-Zsye", culture.Name];
        }

        if (string.IsNullOrWhiteSpace(culture.Name))
        {
            return null;
        }

        return [culture.Name];
    }

    private static bool? GetExplicitPresentation(ReadOnlySpan<char> suffix)
        => suffix.IsEmpty ? null : suffix[0] switch
        {
            (char)VariationSelector15 => false,
            (char)VariationSelector16 => true,
            _ => null,
        };

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(TerminalFontResolver));
        }
    }

    private readonly record struct FontFallbackCacheKey(
        nint PrimaryHandle,
        int Weight,
        int Width,
        SKFontStyleSlant Slant,
        int Codepoint,
        string CultureName,
        bool PreferEmojiPresentation);

    private readonly record struct FontFallbackCacheEntry(SKTypeface? FallbackTypeface, bool Owned = true)
    {
        public static FontFallbackCacheEntry NoFallback { get; } = new(null);
    }
}
