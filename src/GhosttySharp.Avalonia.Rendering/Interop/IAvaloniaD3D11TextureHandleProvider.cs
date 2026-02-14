// Licensed under the MIT License.
// GhosttySharp.Avalonia.Rendering - D3D11 texture handle abstraction for Avalonia interop.

using Avalonia.Platform;
using Avalonia.Skia;

namespace GhosttySharp.Avalonia.Rendering.Interop;

/// <summary>
/// Resolves active D3D11 interop handles for the current Avalonia Skia lease.
/// </summary>
public interface IAvaloniaD3D11TextureHandleProvider
{
    /// <summary>
    /// Attempts to resolve the current D3D11 handles.
    /// </summary>
    /// <param name="lease">Active Skia API lease.</param>
    /// <param name="context">Current platform graphics context.</param>
    /// <param name="deviceHandle">Resolved D3D11 device handle.</param>
    /// <param name="textureHandle">Resolved target texture handle.</param>
    /// <param name="targetViewHandle">Resolved render-target-view handle.</param>
    /// <returns><see langword="true"/> when all required handles are available.</returns>
    bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint textureHandle,
        out nint targetViewHandle);
}

