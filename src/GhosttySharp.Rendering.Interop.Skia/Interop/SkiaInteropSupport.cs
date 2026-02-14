// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop.Skia - Backend-aware interop path selector.

using GhosttySharp.Rendering.Contracts;

namespace GhosttySharp.Rendering.Interop.Skia;

/// <summary>
/// Determines whether a render target can use direct texture interop for a given backend.
/// </summary>
internal static class SkiaInteropSupport
{
    public static bool CanUseDirectTextureInterop(
        in RenderTargetDescriptor descriptor,
        RenderBackendKind surfaceBackend)
    {
        if (descriptor.BackendKind != surfaceBackend)
        {
            return false;
        }

        if (descriptor.TargetKind != RenderTargetKind.Texture2D ||
            descriptor.Width <= 0 ||
            descriptor.Height <= 0 ||
            descriptor.DeviceHandle == nint.Zero ||
            descriptor.TargetHandle == nint.Zero)
        {
            return false;
        }

        return descriptor.BackendKind switch
        {
            RenderBackendKind.Metal => true,
            RenderBackendKind.Vulkan =>
                descriptor.CommandQueueHandle != nint.Zero &&
                descriptor.TargetViewHandle != nint.Zero,
            RenderBackendKind.D3D11 =>
                descriptor.TargetViewHandle != nint.Zero,
            RenderBackendKind.D3D12 =>
                descriptor.CommandQueueHandle != nint.Zero &&
                descriptor.CommandBufferHandle != nint.Zero &&
                descriptor.TargetViewHandle != nint.Zero,
            _ => false,
        };
    }
}
