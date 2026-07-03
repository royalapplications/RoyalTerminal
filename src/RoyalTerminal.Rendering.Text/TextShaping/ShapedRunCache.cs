// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Cache for shaped terminal text runs.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

internal readonly record struct ShapedRunCacheKey(
    ulong TextHash,
    int TextLength,
    nint TypefaceHandle,
    int FontSizeBits,
    int CellWidthBits,
    int CellHeightBits,
    TextDirectionMode Direction,
    bool EnableLigatures);

internal sealed class CachedShapedRun : IDisposable
{
    public CachedShapedRun(
        string text,
        ushort[] glyphIds,
        int[] clusterIndexes,
        float[] xOffsets,
        float[] yOffsets,
        float totalAdvanceX,
        bool naturalSingleWidthCellAligned,
        float clipPadding = 0f,
        SKTextBlob? naturalTextBlob = null)
    {
        Text = text;
        GlyphIds = glyphIds;
        ClusterIndexes = clusterIndexes;
        XOffsets = xOffsets;
        YOffsets = yOffsets;
        TotalAdvanceX = totalAdvanceX;
        NaturalSingleWidthCellAligned = naturalSingleWidthCellAligned;
        ClipPadding = clipPadding;
        NaturalTextBlob = naturalTextBlob;
    }

    public string Text { get; }

    public ushort[] GlyphIds { get; }

    public int[] ClusterIndexes { get; }

    public float[] XOffsets { get; }

    public float[] YOffsets { get; }

    public float TotalAdvanceX { get; }

    public bool NaturalSingleWidthCellAligned { get; }

    public float ClipPadding { get; }

    public SKTextBlob? NaturalTextBlob { get; }

    public int GlyphCount => GlyphIds.Length;

    private int _gridTextBlobRunWidthBits;

    private ulong _gridTextBlobPlacementHash;

    private SKTextBlob? _gridTextBlob;

    public bool TryGetGridTextBlob(int runWidthBits, ulong placementHash, out SKTextBlob textBlob)
    {
        if (_gridTextBlob is { } cachedBlob &&
            _gridTextBlobRunWidthBits == runWidthBits &&
            _gridTextBlobPlacementHash == placementHash)
        {
            textBlob = cachedBlob;
            return true;
        }

        textBlob = null!;
        return false;
    }

    public void SetGridTextBlob(int runWidthBits, ulong placementHash, SKTextBlob textBlob)
    {
        if (_gridTextBlob is { } existing && !ReferenceEquals(existing, textBlob))
        {
            existing.Dispose();
        }

        _gridTextBlobRunWidthBits = runWidthBits;
        _gridTextBlobPlacementHash = placementHash;
        _gridTextBlob = textBlob;
    }

    public void Dispose()
    {
        NaturalTextBlob?.Dispose();
        _gridTextBlob?.Dispose();
    }
}

internal sealed class ShapedRunCache
{
    private readonly Dictionary<ShapedRunCacheKey, CachedShapedRun> _cache = new();
    private readonly int _maxEntries;
    private readonly object _sync = new();

    public ShapedRunCache(int maxEntries = 4096)
    {
        _maxEntries = Math.Max(64, maxEntries);
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _cache.Count;
            }
        }
    }

    public bool TryGet(ShapedRunCacheKey key, ReadOnlySpan<char> text, out CachedShapedRun run)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out run!) && text.SequenceEqual(run.Text.AsSpan()))
            {
                return true;
            }

            run = null!;
            return false;
        }
    }

    public void Store(ShapedRunCacheKey key, CachedShapedRun run)
    {
        lock (_sync)
        {
            if (_cache.Count >= _maxEntries)
            {
                ClearCore();
            }

            StoreCore(key, run);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            ClearCore();
        }
    }

    private void StoreCore(ShapedRunCacheKey key, CachedShapedRun run)
    {
        if (_cache.TryGetValue(key, out CachedShapedRun? existing) &&
            !ReferenceEquals(existing, run))
        {
            existing.Dispose();
        }

        _cache[key] = run;
    }

    private void ClearCore()
    {
        foreach (CachedShapedRun run in _cache.Values)
        {
            run.Dispose();
        }

        _cache.Clear();
    }
}
