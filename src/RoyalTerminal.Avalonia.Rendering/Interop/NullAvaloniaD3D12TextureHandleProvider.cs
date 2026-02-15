// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Rendering - Default no-op D3D12 texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.Interop;

/// <summary>
/// Default resolver that reports no available D3D12 texture handles.
/// </summary>
public sealed class NullAvaloniaD3D12TextureHandleProvider : IAvaloniaD3D12TextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static NullAvaloniaD3D12TextureHandleProvider Instance { get; } = new();

    private NullAvaloniaD3D12TextureHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint commandListHandle,
        out nint textureHandle,
        out nint targetViewHandle)
    {
        deviceHandle = nint.Zero;
        commandQueueHandle = nint.Zero;
        commandListHandle = nint.Zero;
        textureHandle = nint.Zero;
        targetViewHandle = nint.Zero;
        return false;
    }
}

