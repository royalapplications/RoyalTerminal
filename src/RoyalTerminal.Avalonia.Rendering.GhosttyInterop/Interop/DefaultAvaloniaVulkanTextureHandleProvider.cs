// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Default Vulkan texture handle resolver.

using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Vulkan;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Default resolver that attempts to extract active Vulkan image handles from Avalonia's live Skia lease.
/// </summary>
public sealed class DefaultAvaloniaVulkanTextureHandleProvider : IAvaloniaVulkanTextureHandleProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static DefaultAvaloniaVulkanTextureHandleProvider Instance { get; } = new();

    private DefaultAvaloniaVulkanTextureHandleProvider()
    {
    }

    /// <inheritdoc />
    public bool TryGetHandles(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        out nint deviceHandle,
        out nint commandQueueHandle,
        out nint textureHandle,
        out nint textureViewHandle)
    {
        deviceHandle = nint.Zero;
        commandQueueHandle = nint.Zero;
        textureHandle = nint.Zero;
        textureViewHandle = nint.Zero;

        if (context is not IVulkanPlatformGraphicsContext vulkanContext)
        {
            return false;
        }

        deviceHandle = (nint)vulkanContext.Device.Handle;
        commandQueueHandle = (nint)vulkanContext.Device.MainQueueHandle;

        if (deviceHandle == nint.Zero || commandQueueHandle == nint.Zero)
        {
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetCurrentSkiaSession(lease, out object? skiaSession) || skiaSession is null)
        {
            return false;
        }

        if (!AvaloniaInteropHandleExtraction.TryGetNestedMemberValue(
                skiaSession,
                out object? imageInfo,
                "_vulkanSession",
                "ImageInfo") ||
            imageInfo is null)
        {
            if (!AvaloniaInteropHandleExtraction.TryGetMemberValue(skiaSession, "ImageInfo", out imageInfo) || imageInfo is null)
            {
                return false;
            }
        }

        return AvaloniaInteropHandleExtraction.TryGetHandle(imageInfo, out textureHandle, "Handle") &&
               AvaloniaInteropHandleExtraction.TryGetHandle(imageInfo, out textureViewHandle, "ViewHandle");
    }
}
