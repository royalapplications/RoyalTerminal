// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - D3D12 texture handle abstraction for Avalonia interop.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Resolves active D3D12 interop handles for the current Avalonia Skia lease.
/// </summary>
public interface IAvaloniaD3D12TextureHandleProvider
{
    /// <summary>
    /// Attempts to resolve the current D3D12 handles.
    /// </summary>
    /// <param name="lease">Active Skia API lease.</param>
    /// <param name="context">Current platform graphics context.</param>
    /// <param name="deviceHandle">Resolved D3D12 device handle.</param>
    /// <param name="commandQueueHandle">Resolved D3D12 command queue handle.</param>
    /// <param name="commandListHandle">Resolved D3D12 command-list handle.</param>
    /// <param name="textureHandle">Resolved target texture handle.</param>
    /// <param name="targetViewHandle">Resolved render-target-view handle.</param>
    /// <returns><see langword="true"/> when all required handles are available.</returns>
    bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint commandListHandle,
        out nint textureHandle,
        out nint targetViewHandle);
}

