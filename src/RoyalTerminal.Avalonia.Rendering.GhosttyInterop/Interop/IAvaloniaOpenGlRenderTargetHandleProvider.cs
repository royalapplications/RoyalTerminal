// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - OpenGL render-target handle abstraction for Avalonia interop.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Resolves active OpenGL interop handles for the current Avalonia Skia lease.
/// </summary>
public interface IAvaloniaOpenGlRenderTargetHandleProvider
{
    /// <summary>
    /// Attempts to resolve the current OpenGL handles.
    /// </summary>
    /// <param name="lease">Active Skia API lease.</param>
    /// <param name="context">Current platform graphics context.</param>
    /// <param name="contextHandle">Resolved OpenGL context handle.</param>
    /// <param name="framebufferHandle">Resolved OpenGL framebuffer handle (0 for default framebuffer).</param>
    /// <returns><see langword="true"/> when required handles are available.</returns>
    bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint contextHandle,
        out nint framebufferHandle);
}
