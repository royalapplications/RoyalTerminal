// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Default Metal texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Default resolver that attempts to extract active Metal handles from Avalonia's live Skia lease.
/// </summary>
public sealed class DefaultAvaloniaMetalTextureHandleProvider : IAvaloniaMetalTextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static DefaultAvaloniaMetalTextureHandleProvider Instance { get; } = new();

    private DefaultAvaloniaMetalTextureHandleProvider()
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

        if (!AvaloniaInteropHandleExtraction.TryGetHandle(context, out deviceHandle, "Device"))
        {
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetHandle(
                context,
                out commandQueueHandle,
                "CommandQueue",
                "Queue"))
        {
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetCurrentSkiaSession(lease, out object? skiaSession) || skiaSession is null)
        {
            return false;
        }

        if (AvaloniaInteropHandleExtraction.TryGetNestedHandle(skiaSession, out textureHandle, "_session", "Texture") ||
            AvaloniaInteropHandleExtraction.TryGetNestedHandle(skiaSession, out textureHandle, "Session", "Texture"))
        {
            return true;
        }

        return AvaloniaInteropHandleExtraction.TryGetHandle(skiaSession, out textureHandle, "Texture");
    }
}
