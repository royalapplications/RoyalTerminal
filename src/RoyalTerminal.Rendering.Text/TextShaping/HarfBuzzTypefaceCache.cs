// Licensed under the MIT License.
// GhosttySharp.Avalonia - HarfBuzz typeface cache for Skia typefaces.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using HarfBuzzSharp;
using SkiaSharp;

namespace GhosttySharp.Avalonia.Rendering;

/// <summary>
/// Caches HarfBuzz face/font objects for <see cref="SKTypeface"/> instances.
/// </summary>
public sealed class HarfBuzzTypefaceCache : IDisposable
{
    private readonly ConcurrentDictionary<SKTypeface, HarfBuzzTypefaceEntry> _cache =
        new(ReferenceEqualityComparer.Instance);
    private int _disposeState;

    /// <summary>
    /// Gets the number of cached typeface entries.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Gets an existing cached HarfBuzz entry for the typeface, or creates one.
    /// </summary>
    public HarfBuzzTypefaceEntry GetOrCreate(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ThrowIfDisposed();

        return _cache.GetOrAdd(typeface, static tf => new HarfBuzzTypefaceEntry(tf));
    }

    /// <summary>
    /// Disposes all cached HarfBuzz objects.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        foreach (SKTypeface key in _cache.Keys)
        {
            if (_cache.TryRemove(key, out HarfBuzzTypefaceEntry? entry))
            {
                entry.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(HarfBuzzTypefaceCache));
        }
    }
}

/// <summary>
/// HarfBuzz face/font wrapper for a single <see cref="SKTypeface"/>.
/// </summary>
public sealed class HarfBuzzTypefaceEntry : IDisposable
{
    private readonly SKTypeface _typeface;
    private int _disposeState;

    internal HarfBuzzTypefaceEntry(SKTypeface typeface)
    {
        _typeface = typeface;
        UnitsPerEm = Math.Max(1, typeface.UnitsPerEm);

        Face = new Face(GetTable) { UnitsPerEm = UnitsPerEm };
        Font = new Font(Face);
        Font.SetFunctionsOpenType();
        Font.SetScale(UnitsPerEm, UnitsPerEm);
    }

    /// <summary>
    /// Gets the source Skia typeface.
    /// </summary>
    public SKTypeface Typeface => _typeface;

    /// <summary>
    /// Gets the typeface units per em used for HarfBuzz scaling.
    /// </summary>
    public int UnitsPerEm { get; }

    internal Face Face { get; }

    internal Font Font { get; }

    /// <summary>
    /// Disposes the HarfBuzz face/font objects.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Font.Dispose();
        Face.Dispose();
    }

    private Blob? GetTable(Face _, Tag tag)
    {
        if (!_typeface.TryGetTableData((uint)tag, out byte[]? tableData) ||
            tableData is null ||
            tableData.Length == 0)
        {
            return null;
        }

        GCHandle handle = default;
        try
        {
            handle = GCHandle.Alloc(tableData, GCHandleType.Pinned);
            IntPtr pointer = handle.AddrOfPinnedObject();
            ReleaseDelegate release = handle.Free;
            return new Blob(pointer, tableData.Length, MemoryMode.ReadOnly, release);
        }
        catch
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            throw;
        }
    }
}
