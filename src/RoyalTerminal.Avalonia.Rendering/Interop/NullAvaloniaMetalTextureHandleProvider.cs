// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering - Default no-op Metal texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.Interop;

/// <summary>
/// Default resolver that reports no available Metal texture handle.
/// </summary>
public sealed class NullAvaloniaMetalTextureHandleProvider : IAvaloniaMetalTextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static NullAvaloniaMetalTextureHandleProvider Instance { get; } = new();

    private NullAvaloniaMetalTextureHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint textureHandle)
    {
        deviceHandle = nint.Zero;
        commandQueueHandle = nint.Zero;
        textureHandle = nint.Zero;
        return false;
    }
}
