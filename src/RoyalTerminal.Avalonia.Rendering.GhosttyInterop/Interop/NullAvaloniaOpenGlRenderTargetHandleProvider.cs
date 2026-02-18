// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Default no-op OpenGL render-target handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Default resolver that reports no available OpenGL render-target handles.
/// </summary>
public sealed class NullAvaloniaOpenGlRenderTargetHandleProvider : IAvaloniaOpenGlRenderTargetHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static NullAvaloniaOpenGlRenderTargetHandleProvider Instance { get; } = new();

    private NullAvaloniaOpenGlRenderTargetHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint contextHandle,
        out nint framebufferHandle)
    {
        contextHandle = nint.Zero;
        framebufferHandle = nint.Zero;
        return false;
    }
}
