// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal rendering with SkiaSharp.

using System.Collections.Concurrent;
using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// LRU glyph cache using SkiaSharp for high-performance text rendering.
/// Caches shaped glyph bitmaps to avoid re-rasterization every frame.
/// Thread-safe for concurrent read access with exclusive write.
/// </summary>
public sealed class GlyphCache : IDisposable
{
    private readonly record struct GlyphKey(ushort GlyphId, float Size, SKColor Color, bool Bold, bool Italic);

    private readonly ConcurrentDictionary<GlyphKey, SKImage> _cache = new();
    private readonly int _maxEntries;
    private readonly SKTypeface _regularTypeface;
    private readonly SKTypeface? _boldTypeface;
    private readonly SKTypeface? _italicTypeface;
    private readonly SKTypeface? _boldItalicTypeface;
    private TerminalFontRenderingSettings _fontRenderingSettings;
    private bool _disposed;

    /// <summary>The default font used for rendering.</summary>
    public SKTypeface RegularTypeface => _regularTypeface;

    /// <summary>Number of cached glyphs.</summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets or sets the font rendering quality settings used for new <see cref="SKFont"/> instances.
    /// Changing this value clears cached glyph images.
    /// </summary>
    public TerminalFontRenderingSettings FontRenderingSettings
    {
        get => _fontRenderingSettings;
        set
        {
            TerminalFontRenderingSettings next = NormalizeFontRenderingSettings(value);
            if (Equals(_fontRenderingSettings, next))
            {
                return;
            }

            _fontRenderingSettings = next;
            Clear();
        }
    }

    /// <summary>
    /// Creates a new glyph cache with the specified font family and max entries.
    /// </summary>
    /// <param name="fontFamily">Font family name (e.g., "JetBrains Mono", "Cascadia Code").</param>
    /// <param name="maxEntries">Maximum number of cached glyph images before eviction.</param>
    public GlyphCache(string fontFamily = "Consolas", int maxEntries = 8192)
        : this(fontFamily, TerminalFontSource.System, fontFilePath: null, maxEntries)
    {
    }

    /// <summary>
    /// Creates a new glyph cache with the specified font source and max entries.
    /// </summary>
    /// <param name="fontFamily">System font family fallback.</param>
    /// <param name="fontSource">Source used to resolve the primary typeface.</param>
    /// <param name="fontFilePath">Font file path used when <paramref name="fontSource"/> is <see cref="TerminalFontSource.File"/>.</param>
    /// <param name="maxEntries">Maximum number of cached glyph images before eviction.</param>
    public GlyphCache(
        string fontFamily,
        TerminalFontSource fontSource,
        string? fontFilePath,
        int maxEntries = 8192,
        TerminalFontRenderingSettings? fontRenderingSettings = null)
    {
        _maxEntries = maxEntries;
        _fontRenderingSettings = NormalizeFontRenderingSettings(fontRenderingSettings);

        string normalizedFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? "Consolas"
            : fontFamily.Trim();

        if (fontSource == TerminalFontSource.File &&
            TryCreateTypefaceFromFile(fontFilePath) is { } fileTypeface)
        {
            _regularTypeface = fileTypeface;
            _boldTypeface = null;
            _italicTypeface = null;
            _boldItalicTypeface = null;
            return;
        }

        _regularTypeface = CreateSystemTypeface(normalizedFamily, SKFontStyle.Normal);
        _boldTypeface = SKTypeface.FromFamilyName(normalizedFamily, SKFontStyle.Bold);
        _italicTypeface = SKTypeface.FromFamilyName(normalizedFamily, SKFontStyle.Italic);
        _boldItalicTypeface = SKTypeface.FromFamilyName(normalizedFamily, SKFontStyle.BoldItalic);
    }

    /// <summary>
    /// Gets the appropriate typeface for the given style combination.
    /// </summary>
    public SKTypeface GetTypeface(bool bold, bool italic)
    {
        if (bold && italic) return _boldItalicTypeface ?? _boldTypeface ?? _regularTypeface;
        if (bold) return _boldTypeface ?? _regularTypeface;
        if (italic) return _italicTypeface ?? _regularTypeface;
        return _regularTypeface;
    }

    /// <summary>
    /// Creates a configured <see cref="SKFont"/> for measuring and rendering.
    /// </summary>
    public SKFont CreateFont(float size, bool bold = false, bool italic = false)
    {
        var typeface = GetTypeface(bold, italic);
        return CreateFont(typeface, size, _fontRenderingSettings);
    }

    /// <summary>
    /// Creates a configured <see cref="SKFont"/> for a specific typeface.
    /// </summary>
    public static SKFont CreateFont(SKTypeface typeface, float size)
        => CreateFont(typeface, size, TerminalFontRenderingSettings.Default);

    /// <summary>
    /// Creates a configured <see cref="SKFont"/> for a specific typeface and rendering settings.
    /// </summary>
    public static SKFont CreateFont(
        SKTypeface typeface,
        float size,
        TerminalFontRenderingSettings? fontRenderingSettings)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        TerminalFontRenderingSettings settings = NormalizeFontRenderingSettings(fontRenderingSettings);
        return new SKFont(typeface, size)
        {
            Subpixel = settings.SubpixelPositioning,
            Edging = MapFontEdging(settings.Edging),
            Hinting = MapFontHinting(settings.Hinting),
            BaselineSnap = settings.BaselineSnap,
            EmbeddedBitmaps = settings.EmbeddedBitmaps,
            Embolden = settings.Embolden,
            ForceAutoHinting = settings.ForceAutoHinting,
            LinearMetrics = settings.LinearMetrics,
        };
    }

    /// <summary>
    /// Measures the cell size for the current font at the given size.
    /// Returns (cellWidth, cellHeight) suitable for a monospace grid.
    /// </summary>
    public (float Width, float Height) MeasureCellSize(float fontSize)
    {
        using var font = CreateFont(fontSize);
        var metrics = font.Metrics;
        var height = metrics.Descent - metrics.Ascent + metrics.Leading;
        var width = font.MeasureText("M");
        return (width, height);
    }

    /// <summary>
    /// Evicts entries when the cache exceeds the maximum size.
    /// Uses a simple clear strategy for simplicity; LRU could be added with a linked list.
    /// </summary>
    public void EvictIfNeeded()
    {
        if (_cache.Count <= _maxEntries) return;

        // Simple eviction: clear entire cache when it gets too large
        // A production implementation could use a linked list for true LRU
        foreach (var kvp in _cache)
        {
            if (_cache.TryRemove(kvp.Key, out var image))
                image.Dispose();
        }
    }

    /// <summary>
    /// Clears all cached glyphs and frees their GPU/CPU resources.
    /// </summary>
    public void Clear()
    {
        foreach (var kvp in _cache)
        {
            if (_cache.TryRemove(kvp.Key, out var image))
                image.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Clear();
        _regularTypeface.Dispose();
        _boldTypeface?.Dispose();
        _italicTypeface?.Dispose();
        _boldItalicTypeface?.Dispose();
    }

    private static SKTypeface CreateSystemTypeface(string fontFamily, SKFontStyle style)
    {
        return SKTypeface.FromFamilyName(fontFamily, style) ?? SKTypeface.Default;
    }

    private static SKTypeface? TryCreateTypefaceFromFile(string? fontFilePath)
    {
        if (string.IsNullOrWhiteSpace(fontFilePath) || !File.Exists(fontFilePath))
        {
            return null;
        }

        try
        {
            return SKTypeface.FromFile(fontFilePath, 0);
        }
        catch
        {
            return null;
        }
    }

    private static TerminalFontRenderingSettings NormalizeFontRenderingSettings(
        TerminalFontRenderingSettings? settings)
    {
        return (settings ?? TerminalFontRenderingSettings.Default).Normalize();
    }

    private static SKFontEdging MapFontEdging(TerminalFontEdging edging)
    {
        return edging switch
        {
            TerminalFontEdging.Alias => SKFontEdging.Alias,
            TerminalFontEdging.Antialias => SKFontEdging.Antialias,
            _ => SKFontEdging.SubpixelAntialias,
        };
    }

    private static SKFontHinting MapFontHinting(TerminalFontHinting hinting)
    {
        return hinting switch
        {
            TerminalFontHinting.None => SKFontHinting.None,
            TerminalFontHinting.Normal => SKFontHinting.Normal,
            TerminalFontHinting.Full => SKFontHinting.Full,
            _ => SKFontHinting.Slight,
        };
    }
}
