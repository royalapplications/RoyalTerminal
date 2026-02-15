// Licensed under the MIT License.
// GhosttySharp.Avalonia - Terminal font fallback resolver.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using SkiaSharp;

namespace GhosttySharp.Avalonia.Rendering;

/// <summary>
/// Result of resolving a typeface for a codepoint.
/// </summary>
public readonly record struct TerminalFontResolution(SKTypeface Typeface, bool UsedFallback);

/// <summary>
/// Resolves primary/fallback typefaces for terminal text rendering.
/// </summary>
public sealed class TerminalFontResolver : IDisposable
{
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

        if (primaryTypeface.ContainsGlyph(codepoint))
        {
            return new TerminalFontResolution(primaryTypeface, UsedFallback: false);
        }

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
            cultureName);

        FontFallbackCacheEntry entry;
        lock (_sync)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(TerminalFontResolver));
            }

            if (!_fallbackCache.TryGetValue(key, out entry))
            {
                entry = CreateFallbackEntry(primaryTypeface, codepoint, usedCulture);
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
        CultureInfo culture)
    {
        string[]? languageTags = GetLanguageTags(culture);
        string? familyName = string.IsNullOrWhiteSpace(primaryTypeface.FamilyName)
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

    private static string[]? GetLanguageTags(CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(culture.Name))
        {
            return null;
        }

        return [culture.Name];
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
        string CultureName);

    private readonly record struct FontFallbackCacheEntry(SKTypeface? FallbackTypeface)
    {
        public static FontFallbackCacheEntry NoFallback { get; } = new(null);
    }
}
