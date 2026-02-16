// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty - Managed render context wrapper.

using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Ghostty.Native;

namespace RoyalTerminal.Rendering.Interop.Ghostty;

/// <summary>
/// Owns a native renderer context used to create render surfaces.
/// </summary>
public sealed class GhosttyRenderContext : IDisposable
{
    private readonly GhosttyRenderContextHandle _handle;
    private bool _disposed;

    /// <summary>
    /// Initializes a new native render context.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the native context cannot be created.</exception>
    public GhosttyRenderContext()
    {
        GhosttyRendererNativeLibraryLoader.Initialize();
        nint handle = GhosttyRendererNative.ContextNew();
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create native renderer context.");
        }

        _handle = new GhosttyRenderContextHandle(handle);
    }

    /// <summary>
    /// Creates a renderer surface for the specified backend.
    /// </summary>
    /// <param name="backendKind">Requested backend.</param>
    /// <returns>A managed surface wrapper.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when backend kind is unknown.</exception>
    /// <exception cref="InvalidOperationException">Thrown when surface creation fails.</exception>
    public GhosttyRenderSurface CreateSurface(RenderBackendKind backendKind)
    {
        ThrowIfDisposed();

        if (backendKind == RenderBackendKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(backendKind), "Backend kind must be specified.");
        }

        nint surfaceHandle = GhosttyRendererNative.SurfaceNew(_handle.DangerousGetHandle(), backendKind);
        if (surfaceHandle == nint.Zero)
        {
            throw new InvalidOperationException($"Failed to create native renderer surface for backend '{backendKind}'.");
        }

        GhosttyRenderSurfaceHandle handle = new(surfaceHandle);
        return new GhosttyRenderSurface(this, handle, backendKind);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _handle.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
