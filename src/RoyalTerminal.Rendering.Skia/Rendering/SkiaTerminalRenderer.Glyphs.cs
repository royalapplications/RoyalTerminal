// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RoyalTerminal.Terminal.Glyphs;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    private readonly SkiaTerminalGlyphCoverageSource _glyphCoverageSource;

    /// <summary>
    /// Gets this renderer's separately synchronized font coverage source for VT
    /// queries. It is owned by the renderer and returns false after disposal.
    /// </summary>
    public RoyalTerminal.Terminal.ITerminalGlyphCoverageSource GlyphCoverageSource => _glyphCoverageSource;

    private readonly Dictionary<uint, (TerminalGlyphRegistration Entry, SKPath Path)> _registeredGlyphBindings = [];
    private readonly List<uint> _staleGlyphBindings = [];
    private TerminalScreen? _registeredGlyphScreen;
    private ulong _registeredGlyphRevision;

    internal int RegisteredGlyphPathCount => _registeredGlyphBindings.Count;

    private void PrepareRegisteredGlyphCache(TerminalScreen screen)
    {
        if (!ReferenceEquals(screen, _registeredGlyphScreen))
        {
            ClearRegisteredGlyphCache();
            _registeredGlyphScreen = screen;
        }
        else if (_registeredGlyphRevision == screen.GlyphRevision) return;
        _registeredGlyphRevision = screen.GlyphRevision;
        foreach ((uint codepoint, var cached) in _registeredGlyphBindings)
        {
            if (screen.TryGetRegisteredGlyph(codepoint, out TerminalGlyphRegistration? current) && ReferenceEquals(cached.Entry, current)) continue;
            cached.Path.Dispose();
            _staleGlyphBindings.Add(codepoint);
        }
        foreach (uint codepoint in _staleGlyphBindings) _registeredGlyphBindings.Remove(codepoint);
        _staleGlyphBindings.Clear();
    }

    private void ClearRegisteredGlyphCache()
    {
        foreach (var cached in _registeredGlyphBindings.Values) cached.Path.Dispose();
        _registeredGlyphBindings.Clear();
        _staleGlyphBindings.Clear();
    }

    private static bool TryGetRegisteredGlyph(TerminalScreen screen, in TerminalCell cell,
        [NotNullWhen(true)] out TerminalGlyphRegistration? glyph)
    {
        glyph = null;
        if (screen.RegisteredGlyphCount == 0 || cell.Width == 0) return false;
        int codepoint = cell.Codepoint;
        if (!string.IsNullOrEmpty(cell.Grapheme))
        {
            // A registration replaces one scalar, not a shaped multi-scalar
            // cluster. Keep the font path for clusters so marks are never lost.
            if (Rune.DecodeFromUtf16(cell.Grapheme, out Rune rune, out int consumed) != OperationStatus.Done || consumed != cell.Grapheme.Length)
                return false;
            codepoint = rune.Value;
        }
        if (codepoint < 0xE000) return false;
        return screen.TryGetRegisteredGlyph((uint)codepoint, out glyph);
    }

    private void DrawRegisteredGlyph(SKCanvas canvas, uint codepoint, TerminalGlyphRegistration glyph, int column, float y, byte cellSpan, SKColor color)
    {
        if (!TerminalGlyphGeometry.TryPlace(glyph, _cellWidth, _cellHeight, cellSpan, out TerminalGlyphPlacement placement)) return;
        if (!_registeredGlyphBindings.TryGetValue(codepoint, out var cached))
        {
            cached = (glyph, SkiaTerminalGlyphPath.CreateNormalized(glyph.Outline));
            _registeredGlyphBindings.Add(codepoint, cached);
        }
        float x = column * _cellWidth;
        int saved = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(x, y, x + cellSpan * _cellWidth, y + _cellHeight));
            canvas.Translate(x + (float)placement.X, y + (float)(placement.Y + placement.Height));
            canvas.Scale((float)placement.Width, (float)-placement.Height);
            _fgPaint.Color = color;
            canvas.DrawPath(cached.Path, _fgPaint);
        }
        finally { canvas.RestoreToCount(saved); }
    }
}
