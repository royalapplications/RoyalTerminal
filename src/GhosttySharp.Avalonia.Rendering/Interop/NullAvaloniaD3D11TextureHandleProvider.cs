// Licensed under the MIT License.
// GhosttySharp.Avalonia.Rendering - Default no-op D3D11 texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace GhosttySharp.Avalonia.Rendering.Interop;

/// <summary>
/// Default resolver that reports no available D3D11 texture handles.
/// </summary>
public sealed class NullAvaloniaD3D11TextureHandleProvider : IAvaloniaD3D11TextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static NullAvaloniaD3D11TextureHandleProvider Instance { get; } = new();

    private NullAvaloniaD3D11TextureHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint textureHandle,
        out nint targetViewHandle)
    {
        deviceHandle = nint.Zero;
        textureHandle = nint.Zero;
        targetViewHandle = nint.Zero;
        return false;
    }
}

