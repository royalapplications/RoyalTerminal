// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Image rendering diagnostics counters.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Snapshot of terminal image-render diagnostics collected by <see cref="SkiaTerminalRenderer"/>.
/// </summary>
public readonly record struct ImageRenderDiagnostics(
    long PlacementsVisited,
    long PlacementsVisible,
    long Draws,
    long CacheHits,
    long CacheMisses,
    long CacheEvictions,
    int KittyBitmapCacheEntries,
    long KittyBitmapCacheBytes,
    int RasterBitmapCacheEntries,
    long RasterBitmapCacheBytes);
