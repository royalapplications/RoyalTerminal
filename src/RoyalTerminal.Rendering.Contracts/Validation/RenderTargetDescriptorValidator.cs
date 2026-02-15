// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Contracts - Descriptor validation helpers.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Provides invariant validation for <see cref="RenderTargetDescriptor"/> values.
/// </summary>
public static class RenderTargetDescriptorValidator
{
    /// <summary>
    /// Validates a render target descriptor.
    /// </summary>
    public static RenderValidationResult Validate(in RenderTargetDescriptor descriptor)
    {
        if (descriptor.BackendKind == RenderBackendKind.Unknown)
        {
            return RenderValidationResult.Invalid("Backend kind must be specified.");
        }

        if (descriptor.TargetKind == RenderTargetKind.Unknown)
        {
            return RenderValidationResult.Invalid("Target kind must be specified.");
        }

        if (descriptor.Width <= 0)
        {
            return RenderValidationResult.Invalid("Target width must be greater than zero.");
        }

        if (descriptor.Height <= 0)
        {
            return RenderValidationResult.Invalid("Target height must be greater than zero.");
        }

        if (descriptor.SampleCount == 0)
        {
            return RenderValidationResult.Invalid("Sample count must be greater than zero.");
        }

        if (!HasValidTargetHandle(descriptor))
        {
            return RenderValidationResult.Invalid("Target handle must be provided.");
        }

        if (descriptor.TargetKind == RenderTargetKind.Texture2D && descriptor.PixelFormat == RenderPixelFormat.Unknown)
        {
            return RenderValidationResult.Invalid("Texture targets require a known pixel format.");
        }

        switch (descriptor.BackendKind)
        {
            case RenderBackendKind.Metal:
                if (descriptor.DeviceHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("Backend requires a valid device handle.");
                }
                break;

            case RenderBackendKind.Vulkan:
                if (descriptor.DeviceHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("Vulkan backend requires a valid device handle.");
                }

                if (descriptor.CommandQueueHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("Vulkan backend requires a valid command queue handle.");
                }

                if (descriptor.TargetKind == RenderTargetKind.Texture2D && descriptor.TargetViewHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("Vulkan texture targets require a valid image-view handle.");
                }

                break;

            case RenderBackendKind.D3D11:
                if (descriptor.DeviceHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("D3D11 backend requires a valid device handle.");
                }

                if (descriptor.TargetKind == RenderTargetKind.Texture2D && descriptor.TargetViewHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("D3D11 texture targets require a valid render-target view handle.");
                }

                break;

            case RenderBackendKind.D3D12:
                if (descriptor.DeviceHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("D3D12 backend requires a valid device handle.");
                }

                if (descriptor.CommandQueueHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("D3D12 backend requires a valid command queue handle.");
                }

                if (descriptor.CommandBufferHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("D3D12 backend requires a valid command-list handle.");
                }

                if (descriptor.TargetKind == RenderTargetKind.Texture2D && descriptor.TargetViewHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("D3D12 texture targets require a valid render-target view handle.");
                }

                break;

            case RenderBackendKind.OpenGL:
                if (descriptor.ContextHandle == nint.Zero)
                {
                    return RenderValidationResult.Invalid("OpenGL backend requires a valid context handle.");
                }
                break;

            case RenderBackendKind.Software:
                break;

            default:
                return RenderValidationResult.Invalid("Backend kind is not supported by validator.");
        }

        return RenderValidationResult.Valid();
    }

    /// <summary>
    /// Validates a descriptor and throws when invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static void ThrowIfInvalid(in RenderTargetDescriptor descriptor)
    {
        RenderValidationResult result = Validate(descriptor);
        if (!result.IsValid)
        {
            throw new ArgumentException(result.ErrorMessage ?? "Invalid render target descriptor.", nameof(descriptor));
        }
    }

    private static bool HasValidTargetHandle(in RenderTargetDescriptor descriptor)
    {
        // OpenGL default framebuffer (ID 0) is a valid target.
        if (descriptor.BackendKind == RenderBackendKind.OpenGL &&
            descriptor.TargetKind == RenderTargetKind.Framebuffer)
        {
            return true;
        }

        return descriptor.TargetHandle != nint.Zero;
    }
}
