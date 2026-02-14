// Licensed under the MIT License.
// GhosttySharp.Avalonia.Rendering - Vulkan texture handle abstraction for Avalonia interop.

using Avalonia.Platform;
using Avalonia.Skia;

namespace GhosttySharp.Avalonia.Rendering.Interop;

/// <summary>
/// Resolves active Vulkan interop handles for the current Avalonia Skia lease.
/// </summary>
public interface IAvaloniaVulkanTextureHandleProvider
{
    /// <summary>
    /// Attempts to resolve the current Vulkan handles.
    /// </summary>
    /// <param name="lease">Active Skia API lease.</param>
    /// <param name="context">Current platform graphics context.</param>
    /// <param name="deviceHandle">Resolved Vulkan device handle.</param>
    /// <param name="commandQueueHandle">Resolved Vulkan queue handle.</param>
    /// <param name="textureHandle">Resolved target image/texture handle.</param>
    /// <param name="textureViewHandle">Resolved target image-view handle.</param>
    /// <returns><see langword="true"/> when all required handles are available.</returns>
    bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint textureHandle,
        out nint textureViewHandle);
}

