// Licensed under the MIT License.
// RoyalTerminal.Avalonia - Cache for shaped terminal text runs.

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

internal sealed class CachedShapedRun
{
    public CachedShapedRun(
        string text,
        ushort[] glyphIds,
        float[] xOffsets,
        float[] yOffsets,
        float totalAdvanceX)
    {
        Text = text;
        GlyphIds = glyphIds;
        XOffsets = xOffsets;
        YOffsets = yOffsets;
        TotalAdvanceX = totalAdvanceX;
    }

    public string Text { get; }

    public ushort[] GlyphIds { get; }

    public float[] XOffsets { get; }

    public float[] YOffsets { get; }

    public float TotalAdvanceX { get; }

    public int GlyphCount => GlyphIds.Length;
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

    public void Store(
        ShapedRunCacheKey key,
        ReadOnlySpan<char> text,
        ReadOnlySpan<ushort> glyphIds,
        ReadOnlySpan<float> xOffsets,
        ReadOnlySpan<float> yOffsets,
        float totalAdvanceX)
    {
        lock (_sync)
        {
            if (_cache.Count >= _maxEntries)
            {
                _cache.Clear();
            }

            _cache[key] = new CachedShapedRun(
                new string(text),
                glyphIds.ToArray(),
                xOffsets.ToArray(),
                yOffsets.ToArray(),
                totalAdvanceX);
        }
    }

    public void Store(ShapedRunCacheKey key, CachedShapedRun run)
    {
        lock (_sync)
        {
            if (_cache.Count >= _maxEntries)
            {
                _cache.Clear();
            }

            _cache[key] = run;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _cache.Clear();
        }
    }
}
