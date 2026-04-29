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

    private readonly SKFontManager _fontManager;
    private readonly bool _ownsFontManager;
    private readonly Dictionary<FontFallbackCacheKey, FontFallbackCacheEntry> _fallbackCache = new();
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

    public TerminalFontResolver(SKFontManager? fontManager = null)
    {
        _fontManager = fontManager ?? SKFontManager.CreateDefault();
        _ownsFontManager = fontManager is null;
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

        if (!preferEmojiPresentation && charsConsumed == text.Length)
        {
            return ResolveTypefaceCore(primaryTypeface, firstRune.Value, culture, preferEmojiPresentation: false);
        }

        return ResolveTypefaceCore(primaryTypeface, firstRune.Value, culture, preferEmojiPresentation);
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

        if (primaryTypeface.ContainsGlyph(codepoint))
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

        SKFontStyle style = primaryTypeface.FontStyle;
        FontFallbackCacheKey key = new(
            primaryTypeface.Handle,
            primaryTypeface.FamilyName ?? string.Empty,
            (int)style.Weight,
            (int)style.Width,
            style.Slant,
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
            _fontManager.Dispose();
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
        SKTypeface? fallbackTypeface = _fontManager.MatchCharacter(
            familyName,
            primaryTypeface.FontStyle,
            languageTags,
            codepoint);

        if (fallbackTypeface is null)
        {
            return FontFallbackCacheEntry.NoFallback;
        }

        if (!fallbackTypeface.ContainsGlyph(codepoint))
        {
            fallbackTypeface.Dispose();
            return FontFallbackCacheEntry.NoFallback;
        }

        if (ReferenceEquals(fallbackTypeface, primaryTypeface))
        {
            return FontFallbackCacheEntry.NoFallback;
        }

        if (fallbackTypeface.Handle == primaryTypeface.Handle)
        {
            fallbackTypeface.Dispose();
            return FontFallbackCacheEntry.NoFallback;
        }

        return new FontFallbackCacheEntry(fallbackTypeface);
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
        string FamilyName,
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
