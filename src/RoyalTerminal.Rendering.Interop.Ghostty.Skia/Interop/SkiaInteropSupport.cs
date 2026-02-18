// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty.Skia - Backend-aware interop path selector.

using RoyalTerminal.Rendering.Contracts;

namespace RoyalTerminal.Rendering.Interop.Ghostty.Skia;

/// <summary>
/// Determines whether a render target can use direct interop for a given backend/target kind.
/// </summary>
internal static class SkiaInteropSupport
{
    public static bool CanUseDirectInterop(
        in RenderTargetDescriptor descriptor,
        RenderBackendKind surfaceBackend)
    {
        if (descriptor.BackendKind != surfaceBackend)
        {
            return false;
        }

        if (descriptor.Width <= 0 || descriptor.Height <= 0)
        {
            return false;
        }

        return descriptor.BackendKind switch
        {
            RenderBackendKind.Metal =>
                descriptor.TargetKind == RenderTargetKind.Texture2D &&
                descriptor.DeviceHandle != nint.Zero &&
                descriptor.TargetHandle != nint.Zero,

            RenderBackendKind.Vulkan =>
                descriptor.TargetKind == RenderTargetKind.Texture2D &&
                descriptor.DeviceHandle != nint.Zero &&
                descriptor.CommandQueueHandle != nint.Zero &&
                descriptor.TargetHandle != nint.Zero &&
                descriptor.TargetViewHandle != nint.Zero,

            RenderBackendKind.D3D11 =>
                descriptor.TargetKind == RenderTargetKind.Texture2D &&
                descriptor.DeviceHandle != nint.Zero &&
                descriptor.TargetHandle != nint.Zero &&
                descriptor.TargetViewHandle != nint.Zero,

            RenderBackendKind.D3D12 =>
                descriptor.TargetKind == RenderTargetKind.Texture2D &&
                descriptor.DeviceHandle != nint.Zero &&
                descriptor.CommandQueueHandle != nint.Zero &&
                descriptor.CommandBufferHandle != nint.Zero &&
                descriptor.TargetHandle != nint.Zero &&
                descriptor.TargetViewHandle != nint.Zero,

            RenderBackendKind.OpenGL =>
                descriptor.ContextHandle != nint.Zero &&
                (descriptor.TargetKind == RenderTargetKind.Framebuffer ||
                 (descriptor.TargetKind == RenderTargetKind.Texture2D && descriptor.TargetHandle != nint.Zero)),

            RenderBackendKind.Software => false,

            _ => false,
        };
    }

    public static bool SupportsDirectInteropTarget(
        RenderFeatureFlags featureFlags,
        RenderTargetKind targetKind)
    {
        return targetKind switch
        {
            RenderTargetKind.Texture2D =>
                (featureFlags & RenderFeatureFlags.ExternalTextureTargets) != 0,

            RenderTargetKind.Framebuffer =>
                (featureFlags & RenderFeatureFlags.ExternalFramebufferTargets) != 0,

            _ => false,
        };
    }
}
