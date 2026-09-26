// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal font fallback resolver.

using System.Collections.Generic;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Threading;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Result of resolving a typeface for a codepoint.
/// </summary>
public readonly record struct TerminalFontResolution(SKTypeface Typeface, bool UsedFallback);

/// <summary>
/// Resolves primary/fallback typefaces for terminal text rendering.
/// </summary>
public sealed class TerminalFontResolver : IDisposable
{
    private const int RegionalIndicatorStart = 0x1F1E6;
    private const int RegionalIndicatorEnd = 0x1F1FF;
    private const int VariationSelector15 = 0xFE0E;
    private const int VariationSelector16 = 0xFE0F;
    private const int KeycapEnclosingCodepoint = 0x20E3;
    private const int EmojiModifierStart = 0x1F3FB;
    private const int EmojiModifierEnd = 0x1F3FF;
    private const int TagStart = 0xE0020;
    private const int TagEnd = 0xE007F;
    private static readonly string[] s_emojiOnlyLanguageTags = ["und-Zsye"];

    private readonly SKFontManager? _fontManager;
    private readonly ITerminalFontMatcher _fontMatcher;
    private readonly bool _ownsFontManager;
    private readonly Dictionary<FontFallbackCacheKey, FontFallbackCacheEntry> _fallbackCache = new();
    private readonly Dictionary<nint, SKFont> _containsGlyphFontCache = new();
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
    }

    internal TerminalFontResolver(ITerminalFontMatcher fontMatcher)
    {
        ArgumentNullException.ThrowIfNull(fontMatcher);
        _fontMatcher = fontMatcher;
    }

    /// <summary>
    /// Resolves a typeface for a single Unicode codepoint.
    /// </summary>
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

        bool preferEmojiPresentation =
            IsRegionalIndicator(codepoint) ||
            IsDefaultEmojiPresentationCodepoint(codepoint);
        return ResolveTypefaceCore(primaryTypeface, codepoint, culture, preferEmojiPresentation);
    }

    /// <summary>
    /// Resolves a typeface for a UTF-16 text segment.
    /// </summary>
    public TerminalFontResolution ResolveTypeface(
        SKTypeface primaryTypeface,
        ReadOnlySpan<char> text,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(primaryTypeface);
        ThrowIfDisposed();

        if (text.IsEmpty ||
            Rune.DecodeFromUtf16(text, out Rune firstRune, out int charsConsumed) != OperationStatus.Done)
        {
            return new TerminalFontResolution(primaryTypeface, UsedFallback: false);
        }

        bool preferEmojiPresentation =
            ShouldPreferEmojiPresentation(text, firstRune.Value);

        TerminalFontResolution primary = ResolveTypefaceCore(
            primaryTypeface, firstRune.Value, culture, preferEmojiPresentation);
        if (charsConsumed == text.Length || ContainsGrapheme(primary.Typeface, text))
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

            // Additional components do not inherit the base's presentation:
            // emoji fonts may cover a component only in text presentation.
            TerminalFontResolution candidate = ResolveTypefaceCore(
                primaryTypeface, component.Value, culture, preferEmojiPresentation: false);
            if (candidate.Typeface.Handle != primary.Typeface.Handle && ContainsGrapheme(candidate.Typeface, text))
            {
                return candidate;
            }
        }

        // Preserve this API's non-null best-effort fallback when no single
        // available font covers the cluster; missing components remain .notdef.
        return primary;
    }

    /// <summary>
    /// Resolves a typeface for a UTF-16 string segment.
    /// </summary>
    public TerminalFontResolution ResolveTypeface(
        SKTypeface primaryTypeface,
        string text,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ResolveTypeface(primaryTypeface, text.AsSpan(), culture);
    }

    private TerminalFontResolution ResolveTypefaceCore(
        SKTypeface primaryTypeface,
        int codepoint,
        CultureInfo? culture,
        bool preferEmojiPresentation)
    {
        if (preferEmojiPresentation)
        {
            TerminalFontResolution emojiResolution = ResolveCachedFallback(
                primaryTypeface,
                codepoint,
                culture,
                preferEmojiPresentation: true);

            if (emojiResolution.UsedFallback)
            {
                return emojiResolution;
            }
        }

        if (ContainsGlyph(primaryTypeface, codepoint))
        {
            return new TerminalFontResolution(primaryTypeface, UsedFallback: false);
        }

        return ResolveCachedFallback(primaryTypeface, codepoint, culture, preferEmojiPresentation: false);
    }

    private TerminalFontResolution ResolveCachedFallback(
        SKTypeface primaryTypeface,
        int codepoint,
        CultureInfo? culture,
        bool preferEmojiPresentation)
    {
        CultureInfo usedCulture = culture ?? CultureInfo.CurrentUICulture;
        string cultureName = usedCulture.Name;

        FontFallbackCacheKey key = new(
            primaryTypeface.Handle,
            primaryTypeface.FontWeight,
            primaryTypeface.FontWidth,
            primaryTypeface.FontSlant,
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
                    preferEmojiPresentation);
                _fallbackCache.Add(key, entry);
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

            foreach (FontFallbackCacheEntry entry in _fallbackCache.Values)
            {
                if (entry.FallbackTypeface is not { } fallbackTypeface)
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
        bool preferEmojiPresentation)
    {
        string[]? languageTags = GetLanguageTags(culture, preferEmojiPresentation);
        string? familyName = preferEmojiPresentation
            ? null
            : string.IsNullOrWhiteSpace(primaryTypeface.FamilyName)
                ? null
                : primaryTypeface.FamilyName;
        FontFallbackCacheEntry entry = Match(familyName);
        // A file-backed family may not be installed in the system collection.
        // Do not let a failed family-specific match suppress global discovery.
        return entry.FallbackTypeface is null && familyName is not null ? Match(null) : entry;

        FontFallbackCacheEntry Match(string? family)
        {
            SKTypeface? fallbackTypeface = _fontMatcher.MatchCharacter(
                family, primaryTypeface.FontStyle, languageTags, codepoint);
            if (fallbackTypeface is null || ReferenceEquals(fallbackTypeface, primaryTypeface))
                return FontFallbackCacheEntry.NoFallback;
            if (fallbackTypeface.Handle != primaryTypeface.Handle && ContainsGlyph(fallbackTypeface, codepoint))
                return new FontFallbackCacheEntry(fallbackTypeface);

            // Remove the font holding a rejected candidate before releasing it;
            // native handles can be reused by the subsequent global match.
            if (_containsGlyphFontCache.Remove(fallbackTypeface.Handle, out SKFont? font)) font.Dispose();
            fallbackTypeface.Dispose();
            return FontFallbackCacheEntry.NoFallback;
        }
    }

    private bool ContainsGlyph(SKTypeface typeface, int codepoint)
    {
        SKFont font = GetContainsGlyphFont(typeface);
        return font.ContainsGlyph(codepoint);
    }

    private bool ContainsGrapheme(SKTypeface typeface, ReadOnlySpan<char> text)
    {
        SKFont font = GetContainsGlyphFont(typeface);
        while (!text.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(text, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                return false;
            }

            if (!IsGraphemePresentationControl(rune.Value) && !font.ContainsGlyph(rune.Value))
            {
                return false;
            }

            text = text[consumed..];
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

    private static bool IsRegionalIndicator(int codepoint)
    {
        return codepoint >= RegionalIndicatorStart && codepoint <= RegionalIndicatorEnd;
    }

    private static bool ShouldPreferEmojiPresentation(ReadOnlySpan<char> text, int firstCodepoint)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        bool hasTextPresentationSelector = false;
        bool hasEmojiPresentationSelector = false;
        ReadOnlySpan<char> remaining = text;
        while (!remaining.IsEmpty &&
               Rune.DecodeFromUtf16(remaining, out Rune rune, out int charsConsumed) == OperationStatus.Done)
        {
            int codepoint = rune.Value;

            if (codepoint == VariationSelector15)
            {
                hasTextPresentationSelector = true;
            }

            if (codepoint == VariationSelector16)
            {
                hasEmojiPresentationSelector = true;
            }

            if (codepoint == KeycapEnclosingCodepoint ||
                IsEmojiModifier(codepoint) ||
                IsTagCodepoint(codepoint))
            {
                return true;
            }

            remaining = remaining[charsConsumed..];
        }

        if (hasEmojiPresentationSelector)
        {
            return true;
        }

        if (hasTextPresentationSelector)
        {
            return false;
        }

        return IsRegionalIndicator(firstCodepoint) || IsDefaultEmojiPresentationCodepoint(firstCodepoint);
    }

    private static bool IsDefaultEmojiPresentationCodepoint(int codepoint)
    {
        // Most modern emoji are in these blocks; this keeps plain text codepoints
        // on the regular fallback path unless emoji-specific markers are present.
        return codepoint >= 0x1F300 && codepoint <= 0x1FAFF;
    }

    private static bool IsEmojiModifier(int codepoint)
    {
        return codepoint >= EmojiModifierStart && codepoint <= EmojiModifierEnd;
    }

    private static bool IsTagCodepoint(int codepoint)
    {
        return codepoint >= TagStart && codepoint <= TagEnd;
    }

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

    private readonly record struct FontFallbackCacheEntry(SKTypeface? FallbackTypeface)
    {
        public static FontFallbackCacheEntry NoFallback { get; } = new(null);
    }
}
