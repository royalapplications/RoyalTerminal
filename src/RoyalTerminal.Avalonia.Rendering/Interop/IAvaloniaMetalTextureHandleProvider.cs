// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering - Metal texture handle abstraction for Avalonia interop.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.Interop;

/// <summary>
/// Resolves active Metal interop handles for the current Avalonia Skia lease.
/// </summary>
public interface IAvaloniaMetalTextureHandleProvider
{
    /// <summary>
    /// Attempts to resolve the current Metal handles.
    /// </summary>
    /// <param name="lease">Active Skia API lease.</param>
    /// <param name="context">Current platform graphics context.</param>
    /// <param name="deviceHandle">Resolved Metal device handle.</param>
    /// <param name="commandQueueHandle">Resolved Metal command queue handle.</param>
    /// <param name="textureHandle">Resolved target texture handle.</param>
    /// <returns><see langword="true"/> when all handles are available.</returns>
    bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint textureHandle);
}
