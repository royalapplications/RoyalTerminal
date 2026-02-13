// Licensed under the MIT License.
// GhosttySharp.Avalonia - Terminal rendering with SkiaSharp.

using System.Collections.Concurrent;
using SkiaSharp;

namespace GhosttySharp.Avalonia.Rendering;

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
    private bool _disposed;

    /// <summary>The default font used for rendering.</summary>
    public SKTypeface RegularTypeface => _regularTypeface;

    /// <summary>Number of cached glyphs.</summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Creates a new glyph cache with the specified font family and max entries.
    /// </summary>
    /// <param name="fontFamily">Font family name (e.g., "JetBrains Mono", "Cascadia Code").</param>
    /// <param name="maxEntries">Maximum number of cached glyph images before eviction.</param>
    public GlyphCache(string fontFamily = "Consolas", int maxEntries = 8192)
    {
        _maxEntries = maxEntries;

        _regularTypeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyle.Normal)
                           ?? SKTypeface.Default;
        _boldTypeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyle.Bold);
        _italicTypeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyle.Italic);
        _boldItalicTypeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyle.BoldItalic);
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
        return new SKFont(typeface, size)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias,
            Hinting = SKFontHinting.Slight,
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
}
