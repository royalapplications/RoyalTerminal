// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class SkiaTerminalRenderer
{
    // Value-only snapshots: retaining the previous frame must not retain decoded
    // image payloads, screen history, or placement/anchor objects.
    private readonly record struct ImageDamageItem(
        SKRect Destination, SKRect Source, TerminalBitmapCacheKey Pixels, int Layer);

    private List<ImageDamageItem> _lastKittyDamage = new();
    private List<ImageDamageItem> _nextKittyDamage = new();
    private List<ImageDamageItem> _lastRasterDamage = new();
    private List<ImageDamageItem> _nextRasterDamage = new();
    private readonly SKPath _imageDamagePath = new();
    private bool _clipImagesToDamage;
    private double _lastImageDamageFraction;
    private SKRect _lastImageDamageClip;

    /// <summary>
    /// Marks rows intersecting changed image geometry or pixels, including the
    /// previous bounds of moved/deleted images. Call under the screen lock before
    /// clearing a retained canvas. Existing dirty rows are never cleared. The
    /// baseline advances only after a successful <see cref="Render(SKCanvas, TerminalScreen, bool)"/>.
    /// Hosts must still invalidate the whole frame for resize, scroll translation,
    /// theme changes, or a replacement render target. No background is painted here.
    /// </summary>
    /// <param name="screen">Stable screen to prepare for rendering.</param>
    /// <param name="overscan">Requested rows beyond the viewport; fractional scrolling adds one below.</param>
    public void PrepareImageDamage(TerminalScreen screen, TerminalRenderOverscan overscan = default)
    {
        ArgumentNullException.ThrowIfNull(screen);
        overscan = GetImageRenderOverscan(screen, overscan);
        TerminalRenderViewport rows = screen.GetRenderViewport(overscan);
        SKRect clip = GetImageRenderClip(screen, rows.CapturedOverscan);
        _nextKittyDamage.Clear();
        foreach (TerminalKittyImagePlacement placement in screen.GetKittyPlacements(overscan))
        {
            SKRect destination = GetKittyDestination(placement);
            SKRect sourceRect = new(placement.SourceX, placement.SourceY,
                (float)placement.SourceX + placement.SourceWidth, (float)placement.SourceY + placement.SourceHeight);
            if (!IntersectsViewport(destination, clip) || !IsRenderableImageRect(sourceRect, destination) ||
                !screen.TryGetKittyImageSource(placement.ImageId, out TerminalKittyImageSource? source) || source is null) continue;
            _nextKittyDamage.Add(new(destination, sourceRect,
                new(source.WidthPx, source.HeightPx, source.RgbaPixels.Length, source.ContentFingerprint), (int)placement.Layer));
        }

        _nextRasterDamage.Clear();
        foreach (TerminalRasterImagePlacement placement in screen.GetRasterImagePlacements())
        {
            SKRect destination = GetRasterDestination(placement, screen.ViewportTopAbsoluteRow);
            SKRect sourceRect = new(placement.SourceX, placement.SourceY,
                (float)placement.SourceX + placement.SourceWidth, (float)placement.SourceY + placement.SourceHeight);
            if (!IntersectsViewport(destination, clip) || !IsRenderableImageRect(sourceRect, destination) ||
                !screen.TryGetRasterImageSource(placement.ImageId, out TerminalRasterImageSource? source) || source is null) continue;
            _nextRasterDamage.Add(new(destination, sourceRect,
                new(source.WidthPx, source.HeightPx, source.RgbaPixels.Length, source.ContentFingerprint), (int)placement.Layer));
        }

        bool geometryChanged = clip != _lastImageDamageClip || screen.RenderScrollFraction != _lastImageDamageFraction;
        MarkImageChanges(rows, clip, _lastKittyDamage, _nextKittyDamage, geometryChanged);
        MarkImageChanges(rows, clip, _lastRasterDamage, _nextRasterDamage, geometryChanged);
    }

    private void MarkImageChanges(TerminalRenderViewport rows, SKRect clip,
        List<ImageDamageItem> previous, List<ImageDamageItem> current, bool geometryChanged)
    {
        // Sequence comparison also detects stacking-order changes. Insertion can
        // conservatively dirty later placements, but never unrelated text rows.
        for (int index = 0; index < Math.Max(previous.Count, current.Count); index++)
        {
            if (!geometryChanged && index < previous.Count && index < current.Count && previous[index] == current[index]) continue;
            if (index < previous.Count) MarkImageBounds(rows, clip, previous[index].Destination);
            if (index < current.Count) MarkImageBounds(rows, clip, current[index].Destination);
        }
    }

    private void MarkImageBounds(TerminalRenderViewport rows, SKRect clip, SKRect bounds)
    {
        if (!IntersectsViewport(bounds, clip) || _cellHeight <= 0) return;
        int first = Math.Clamp((int)MathF.Floor(Math.Max(bounds.Top, clip.Top) / _cellHeight) + rows.ViewportStart, 0, rows.Count);
        int end = Math.Clamp((int)MathF.Ceiling(Math.Min(bounds.Bottom, clip.Bottom) / _cellHeight) + rows.ViewportStart, 0, rows.Count);
        for (int index = first; index < end; index++) rows[index].Row.IsDirty = true;
    }

    private void BuildImageDamageClip(TerminalRenderViewport rows, SKRect clip, bool forceFullRedraw)
    {
        _imageDamagePath.Rewind();
        _clipImagesToDamage = !forceFullRedraw;
        if (forceFullRedraw || (_nextKittyDamage.Count == 0 && _nextRasterDamage.Count == 0)) return;
        int index = 0;
        while (index < rows.Count)
        {
            if (!rows[index].Row.IsDirty) { index++; continue; }
            int first = rows[index++].ViewportY;
            while (index < rows.Count && rows[index].Row.IsDirty) index++;
            int end = rows[index - 1].ViewportY + 1;
            _imageDamagePath.AddRect(new(clip.Left, first * _cellHeight, clip.Right, end * _cellHeight));
        }
    }

    private void CommitImageDamage(TerminalScreen screen, SKRect clip)
    {
        (_lastKittyDamage, _nextKittyDamage) = (_nextKittyDamage, _lastKittyDamage);
        (_lastRasterDamage, _nextRasterDamage) = (_nextRasterDamage, _lastRasterDamage);
        _nextKittyDamage.Clear();
        _nextRasterDamage.Clear();
        _lastImageDamageFraction = screen.RenderScrollFraction;
        _lastImageDamageClip = clip;
    }

    private static TerminalRenderOverscan GetImageRenderOverscan(TerminalScreen screen, TerminalRenderOverscan overscan)
        => screen.RenderScrollFraction == 0 ? overscan : new(overscan.Above, Math.Max(overscan.Below, (ushort)1));

    private SKRect GetImageRenderClip(TerminalScreen screen, TerminalRenderOverscan actual)
        => new(0, -actual.Above * _cellHeight, screen.Columns * _cellWidth, (screen.ViewportRows + actual.Below) * _cellHeight);

    private SKRect GetKittyDestination(TerminalKittyImagePlacement placement)
    {
        (float xScale, float yScale) = GetKittyPlacementScale(placement);
        float left = placement.ViewportColumn * _cellWidth + placement.XOffsetPx * xScale;
        float top = placement.ViewportRow * _cellHeight + placement.YOffsetPx * yScale;
        return new(left, top, left + placement.WidthPx * xScale, top + placement.HeightPx * yScale);
    }

    private SKRect GetRasterDestination(TerminalRasterImagePlacement placement, int viewportTop)
    {
        float xScale = GetRasterPlacementScale(placement.CellWidthPx, _cellWidth);
        float yScale = GetRasterPlacementScale(placement.CellHeightPx, _cellHeight);
        float left = placement.AnchorColumn * _cellWidth + placement.XOffsetPx * xScale;
        float top = ((long)placement.AnchorRow - viewportTop) * _cellHeight + placement.YOffsetPx * yScale;
        return new(left, top, left + placement.WidthPx * xScale, top + placement.HeightPx * yScale);
    }

    private static void TouchVisibleBitmap(Dictionary<TerminalBitmapCacheKey, TerminalBitmapCacheEntry> cache,
        TerminalBitmapCacheKey key, long frame)
    {
        // Clean visible placements still protect their bitmaps from eviction;
        // clipping must not introduce decode/upload churn on the next dirty row.
        if (cache.TryGetValue(key, out TerminalBitmapCacheEntry? entry)) entry.LastUsedFrame = frame;
    }
}
