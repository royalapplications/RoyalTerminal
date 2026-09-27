// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Platform;
using Avalonia.Skia;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

// A retained framebuffer owns its surface, never Avalonia's shared contexts.
// Composition messages run before Avalonia makes a GPU context current. Retain
// the borrowed context identity from the public lease and re-enter it on release.
internal sealed class TerminalRenderSurface : IDisposable
{
    private readonly GRContext? _grContext;
    private readonly IPlatformGraphicsContext? _platformContext;
    private SKSurface? _surface;

    private TerminalRenderSurface(SKSurface surface, GRContext? grContext,
        IPlatformGraphicsContext? platformContext, long byteCount)
    {
        _surface = surface;
        _grContext = grContext;
        _platformContext = platformContext;
        ByteCount = byteCount;
    }

    internal SKSurface Surface => _surface ?? throw new ObjectDisposedException(nameof(TerminalRenderSurface));
    internal bool IsGpuBacked => _grContext is not null;
    internal long ByteCount { get; private set; }

    internal static TerminalRenderSurface? Create(SKImageInfo info, ISkiaSharpApiLease lease)
    {
        GRContext? grContext = lease.GrContext;
        IPlatformGraphicsContext? platformContext = null;
        if (TerminalShaderPostProcessor.CanUseGpuRenderSurface(grContext))
        {
            using ISkiaSharpPlatformGraphicsApiLease? platformLease = lease.TryLeasePlatformGraphicsApi();
            platformContext = platformLease?.Context;
        }

        // A custom backend without a re-enterable context can still composite a
        // CPU framebuffer. Do not retain GPU objects we cannot safely release.
        if (platformContext is null) grContext = null;
        SKSurface? surface = TerminalShaderPostProcessor.CreateRenderSurface(
            info, grContext, out bool gpuBacked, budgeted: false);
        return surface is null ? null : new(surface, gpuBacked ? grContext : null,
            gpuBacked ? platformContext : null, (long)info.Width * info.Height * info.BytesPerPixel);
    }

    public void Dispose()
    {
        if (_surface is null) return;
        if (_grContext is null)
        {
            Release();
            return;
        }

        // An abandoned/disposed Skia context already retired its GPU resources.
        if (_grContext.Handle == IntPtr.Zero || _grContext.IsAbandoned)
        {
            Release();
            return;
        }

        try
        {
            if (_platformContext!.IsLost) throw new PlatformGraphicsContextLostException();
            using IDisposable current = _platformContext.EnsureCurrent();
            lock (_grContext) Release();
        }
        catch (PlatformGraphicsContextLostException)
        {
            // Only abandon a context the backend declares lost, never another
            // live terminal's context just to reclaim our own framebuffer.
            lock (_grContext)
            {
                if (_grContext.Handle != IntPtr.Zero) _grContext.AbandonContext();
                Release();
            }
        }
    }

    private void Release()
    {
        _surface!.Dispose();
        _surface = null;
        ByteCount = 0;
    }
}
