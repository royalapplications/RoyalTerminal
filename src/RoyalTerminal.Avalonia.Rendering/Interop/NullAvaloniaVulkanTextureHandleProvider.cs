// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Rendering - Default no-op Vulkan texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.Interop;

/// <summary>
/// Default resolver that reports no available Vulkan texture handles.
/// </summary>
public sealed class NullAvaloniaVulkanTextureHandleProvider : IAvaloniaVulkanTextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static NullAvaloniaVulkanTextureHandleProvider Instance { get; } = new();

    private NullAvaloniaVulkanTextureHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint textureHandle,
        out nint textureViewHandle)
    {
        deviceHandle = nint.Zero;
        commandQueueHandle = nint.Zero;
        textureHandle = nint.Zero;
        textureViewHandle = nint.Zero;
        return false;
    }
}

