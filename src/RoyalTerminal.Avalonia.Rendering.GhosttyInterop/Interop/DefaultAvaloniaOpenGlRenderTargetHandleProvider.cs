// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Default OpenGL render-target handle resolver.

using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Default resolver that attempts to extract active OpenGL handles from Avalonia's live Skia lease.
/// </summary>
public sealed class DefaultAvaloniaOpenGlRenderTargetHandleProvider : IAvaloniaOpenGlRenderTargetHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static DefaultAvaloniaOpenGlRenderTargetHandleProvider Instance { get; } = new();

    private const int GlFramebufferBinding = 0x8CA6;

    private DefaultAvaloniaOpenGlRenderTargetHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint contextHandle,
        out nint framebufferHandle)
    {
        framebufferHandle = nint.Zero;

        IGlContext? glContext = context as IGlContext;
        if (glContext is null &&
            AvaloniaInteropHandleExtraction.TryGetCurrentSkiaSession(lease, out object? skiaSession) &&
            skiaSession is not null &&
            AvaloniaInteropHandleExtraction.TryGetNestedMemberValue(skiaSession, out object? nestedGlSession, "_glSession") &&
            nestedGlSession is not null &&
            AvaloniaInteropHandleExtraction.TryGetMemberValue(nestedGlSession, "Context", out object? nestedContext) &&
            nestedContext is IGlContext nestedGlContext)
        {
            glContext = nestedGlContext;
        }

        if (glContext is null)
        {
            contextHandle = nint.Zero;
            return false;
        }

        if (!TryGetContextHandle(glContext, out contextHandle))
        {
            return false;
        }

        TryGetFramebufferBinding(glContext, out framebufferHandle);
        return true;
    }

    private static bool TryGetContextHandle(IGlContext glContext, out nint contextHandle)
    {
        if (glContext is EglContext eglContext)
        {
            contextHandle = eglContext.Context;
            return contextHandle != nint.Zero;
        }

        return AvaloniaInteropHandleExtraction.TryGetHandle(glContext, out contextHandle, "Handle", "Context");
    }

    private static bool TryGetFramebufferBinding(IGlContext glContext, out nint framebufferHandle)
    {
        framebufferHandle = nint.Zero;

        try
        {
            using IDisposable lease = glContext.EnsureCurrent();
            int framebuffer = 0;
            glContext.GlInterface.GetIntegerv(GlFramebufferBinding, out framebuffer);
            framebufferHandle = (nint)framebuffer;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
