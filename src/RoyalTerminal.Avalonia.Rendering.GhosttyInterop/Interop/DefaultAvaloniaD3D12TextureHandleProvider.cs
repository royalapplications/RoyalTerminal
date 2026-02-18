// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Default D3D12 texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Default resolver that attempts to extract active D3D12 handles from Avalonia's live Skia lease.
/// </summary>
public sealed class DefaultAvaloniaD3D12TextureHandleProvider : IAvaloniaD3D12TextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static DefaultAvaloniaD3D12TextureHandleProvider Instance { get; } = new();

    private DefaultAvaloniaD3D12TextureHandleProvider()
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
        textureHandle = nint.Zero;
        targetViewHandle = nint.Zero;

        if (!AvaloniaInteropHandleExtraction.TryGetHandle(
                context,
                out deviceHandle,
                "Device",
                "NativeDevice",
                "D3D12Device"))
        {
            commandQueueHandle = nint.Zero;
            commandListHandle = nint.Zero;
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetHandle(
                context,
                out commandQueueHandle,
                "CommandQueue",
                "Queue",
                "D3D12CommandQueue"))
        {
            commandListHandle = nint.Zero;
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetCurrentSkiaSession(lease, out object? skiaSession) || skiaSession is null)
        {
            commandListHandle = nint.Zero;
            return false;
        }

        object sessionSource = ResolveInnerSession(skiaSession);
        if (!AvaloniaInteropHandleExtraction.TryGetHandle(
                sessionSource,
                out commandListHandle,
                "CommandList",
                "CommandBuffer",
                "CommandListHandle",
                "D3D12CommandList"))
        {
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetHandle(
                sessionSource,
                out textureHandle,
                "D3D12Texture",
                "Texture",
                "TextureHandle",
                "TargetHandle"))
        {
            return false;
        }

        return AvaloniaInteropHandleExtraction.TryGetHandle(
            sessionSource,
            out targetViewHandle,
            "D3D12RenderTargetView",
            "RenderTargetView",
            "TargetView",
            "TargetViewHandle");
    }

    private static object ResolveInnerSession(object skiaSession)
    {
        if (AvaloniaInteropHandleExtraction.TryGetMemberValue(skiaSession, "_session", out object? inner) && inner is not null)
        {
            return inner;
        }

        if (AvaloniaInteropHandleExtraction.TryGetMemberValue(skiaSession, "Session", out inner) && inner is not null)
        {
            return inner;
        }

        if (AvaloniaInteropHandleExtraction.TryGetMemberValue(skiaSession, "_renderSession", out inner) && inner is not null)
        {
            return inner;
        }

        if (AvaloniaInteropHandleExtraction.TryGetMemberValue(skiaSession, "RenderSession", out inner) && inner is not null)
        {
            return inner;
        }

        return skiaSession;
    }
}
