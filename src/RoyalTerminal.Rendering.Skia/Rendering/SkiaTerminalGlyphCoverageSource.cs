// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Thread-safe, lazily initialized font coverage for the configured regular font
/// and Skia terminal fallback policy. Owns its resources independently of drawing.
/// </summary>
public sealed class SkiaTerminalGlyphCoverageSource : ITerminalGlyphCoverageSource, IDisposable
{
    private readonly string _fontFamily;
    private readonly TerminalFontSource _fontSource;
    private readonly string? _fontFilePath;
    private readonly int _cacheCapacity;
    private readonly object _sync = new();
    private readonly Dictionary<uint, bool> _coverage = new();
    private GlyphCache? _fonts;
    private TerminalFontResolver? _resolver;
    private bool _disposed;

    /// <summary>
    /// Creates a source matching a renderer's font selection, with at most 4,096
    /// cached scalar results. Font resources are created on the first valid query.
    /// </summary>
    public SkiaTerminalGlyphCoverageSource(string fontFamily = "Consolas",
        TerminalFontSource fontSource = TerminalFontSource.System, string? fontFilePath = null)
        : this(fontFamily, fontSource, fontFilePath, 4096) { }

    internal SkiaTerminalGlyphCoverageSource(string fontFamily, TerminalFontSource fontSource,
        string? fontFilePath, int cacheCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheCapacity);
        _fontFamily = fontFamily;
        _fontSource = fontSource;
        _fontFilePath = fontFilePath;
        _cacheCapacity = cacheCapacity;
    }

    internal int CachedCount { get { lock (_sync) return _coverage.Count; } }
    internal bool IsInitialized { get { lock (_sync) return _fonts is not null; } }

    /// <inheritdoc />
    /// <remarks>Returns false for invalid scalars and after disposal.</remarks>
    public bool HasSystemGlyph(uint codepoint)
    {
        if (codepoint > 0x10FFFF || codepoint is >= 0xD800 and <= 0xDFFF) return false;
        lock (_sync)
        {
            if (_disposed) return false;
            if (_coverage.TryGetValue(codepoint, out bool found)) return found;
            _fonts ??= new GlyphCache(_fontFamily, _fontSource, _fontFilePath);
            _resolver ??= new TerminalFontResolver();
            if (_coverage.Count == _cacheCapacity)
            {
                // Bound both result and fallback/typeface caches, including
                // negative queries from untrusted terminal applications.
                TerminalFontResolver next = new();
                _resolver.Dispose();
                _resolver = next;
                _coverage.Clear();
            }
            SKTypeface typeface = _resolver.ResolveTypeface(_fonts.RegularTypeface, (int)codepoint,
                CultureInfo.InvariantCulture).Typeface;
            using SKFont font = GlyphCache.CreateFont(typeface, 12);
            found = font.ContainsGlyph((int)codepoint);
            _coverage.Add(codepoint, found);
            return found;
        }
    }

    /// <summary>Releases owned font resources after any active query completes; safe to repeat.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _coverage.Clear();
            _resolver?.Dispose();
            _fonts?.Dispose();
            _resolver = null;
            _fonts = null;
        }
    }
}
