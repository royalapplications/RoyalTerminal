// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - HarfBuzz text shaping infrastructure.

using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using HarfBuzzSharp;
using SkiaSharp;
using HarfBuzzBuffer = HarfBuzzSharp.Buffer;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Direction handling mode used by text shaping.
/// </summary>
public enum TextDirectionMode
{
    Auto = 0,
    LeftToRight = 1,
    RightToLeft = 2,
}

/// <summary>
/// Options for shaping text into glyph runs.
/// </summary>
public readonly record struct TextShapingOptions(
    float FontSize,
    CultureInfo? Culture = null,
    TextDirectionMode Direction = TextDirectionMode.Auto,
    bool EnableLigatures = true);

/// <summary>
/// A shaped glyph with cluster/position information.
/// </summary>
public readonly record struct ShapedGlyph(
    uint GlyphId,
    int Cluster,
    float AdvanceX,
    float OffsetX,
    float OffsetY);

/// <summary>
/// Shaped glyph run output from a text shaper.
/// </summary>
public readonly struct ShapedTextRun
{
    private static readonly ShapedGlyph[] s_emptyGlyphs = [];

    /// <summary>
    /// Empty shaped run.
    /// </summary>
    public static ShapedTextRun Empty { get; } = new(s_emptyGlyphs, 0f);

    public ShapedTextRun(ShapedGlyph[] glyphs, float totalAdvanceX)
    {
        Glyphs = glyphs ?? throw new ArgumentNullException(nameof(glyphs));
        TotalAdvanceX = totalAdvanceX;
    }

    /// <summary>
    /// Gets the shaped glyphs.
    /// </summary>
    public ReadOnlyMemory<ShapedGlyph> Glyphs { get; }

    /// <summary>
    /// Gets the number of shaped glyphs.
    /// </summary>
    public int GlyphCount => Glyphs.Length;

    /// <summary>
    /// Gets the total horizontal advance of the shaped run.
    /// </summary>
    public float TotalAdvanceX { get; }
}

/// <summary>
/// Shapes text into glyph runs.
/// </summary>
public interface ITextShaper
{
    /// <summary>
    /// Shapes the supplied UTF-16 text with the specified typeface and options.
    /// </summary>
    ShapedTextRun Shape(ReadOnlySpan<char> text, SKTypeface typeface, in TextShapingOptions options);
}

/// <summary>
/// HarfBuzz-based implementation of <see cref="ITextShaper"/>.
/// </summary>
public sealed class HarfBuzzTextShaper : ITextShaper, IDisposable
{
    [ThreadStatic]
    private static HarfBuzzBuffer? s_buffer;

    private static readonly ConcurrentDictionary<string, Language> s_cachedLanguage =
        new(StringComparer.Ordinal);
    private static readonly Feature[] s_disableLigaturesFeatures =
    [
        new Feature(Tag.Parse("liga"), 0, 0, uint.MaxValue),
        new Feature(Tag.Parse("clig"), 0, 0, uint.MaxValue),
        new Feature(Tag.Parse("calt"), 0, 0, uint.MaxValue),
    ];

    private readonly HarfBuzzTypefaceCache _typefaceCache;
    private readonly bool _ownsTypefaceCache;
    private int _disposeState;

    public HarfBuzzTextShaper(HarfBuzzTypefaceCache? typefaceCache = null)
    {
        _typefaceCache = typefaceCache ?? new HarfBuzzTypefaceCache();
        _ownsTypefaceCache = typefaceCache is null;
    }

    /// <summary>
    /// Shapes the supplied text.
    /// </summary>
    public ShapedTextRun Shape(ReadOnlySpan<char> text, SKTypeface typeface, in TextShapingOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ThrowIfDisposed();

        if (options.FontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TextShapingOptions.FontSize),
                "Font size must be greater than zero.");
        }

        if (text.IsEmpty)
        {
            return ShapedTextRun.Empty;
        }

        HarfBuzzTypefaceEntry typefaceEntry = _typefaceCache.GetOrCreate(typeface);

        HarfBuzzBuffer buffer = s_buffer ??= new HarfBuzzBuffer();
        buffer.Reset();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();

        Direction? direction = ResolveDirection(options.Direction);
        if (direction.HasValue)
        {
            buffer.Direction = direction.Value;
        }

        CultureInfo usedCulture = options.Culture ?? CultureInfo.CurrentCulture;
        string cultureKey = string.IsNullOrWhiteSpace(usedCulture.Name)
            ? CultureInfo.InvariantCulture.Name
            : usedCulture.Name;
        buffer.Language = s_cachedLanguage.GetOrAdd(
            cultureKey,
            static (_, culture) => new Language(culture),
            usedCulture);

        typefaceEntry.Font.Shape(
            buffer,
            options.EnableLigatures ? Array.Empty<Feature>() : s_disableLigaturesFeatures);

        if (buffer.Direction == Direction.RightToLeft)
        {
            buffer.Reverse();
        }

        int glyphCount = buffer.Length;
        if (glyphCount <= 0)
        {
            return ShapedTextRun.Empty;
        }

        ReadOnlySpan<GlyphInfo> glyphInfos = buffer.GetGlyphInfoSpan();
        ReadOnlySpan<GlyphPosition> glyphPositions = buffer.GetGlyphPositionSpan();
        ShapedGlyph[] glyphs = new ShapedGlyph[glyphCount];

        float textScale = options.FontSize / typefaceEntry.UnitsPerEm;
        float totalAdvanceX = 0;

        for (int i = 0; i < glyphCount; i++)
        {
            GlyphInfo sourceInfo = glyphInfos[i];
            GlyphPosition sourcePosition = glyphPositions[i];

            ShapedGlyph shapedGlyph = new(
                sourceInfo.Codepoint,
                checked((int)sourceInfo.Cluster),
                sourcePosition.XAdvance * textScale,
                sourcePosition.XOffset * textScale,
                -sourcePosition.YOffset * textScale);

            glyphs[i] = shapedGlyph;
            totalAdvanceX += shapedGlyph.AdvanceX;
        }

        return new ShapedTextRun(glyphs, totalAdvanceX);
    }

    /// <summary>
    /// Shapes the supplied UTF-16 string.
    /// </summary>
    public ShapedTextRun Shape(string text, SKTypeface typeface, in TextShapingOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Shape(text.AsSpan(), typeface, in options);
    }

    /// <summary>
    /// Disposes the shaper and owned resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        if (_ownsTypefaceCache)
        {
            _typefaceCache.Dispose();
        }
    }

    private static Direction? ResolveDirection(TextDirectionMode mode)
    {
        return mode switch
        {
            TextDirectionMode.LeftToRight => Direction.LeftToRight,
            TextDirectionMode.RightToLeft => Direction.RightToLeft,
            _ => null,
        };
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(HarfBuzzTextShaper));
        }
    }
}
