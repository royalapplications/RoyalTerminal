// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Shader native texture presentation boundary.

using Avalonia.Skia;
using RoyalTerminal.Shaders;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Imports supported native shader textures into the active Skia GPU context.
/// </summary>
public sealed class TerminalShaderSkiaNativeTexturePresenter : ITerminalShaderNativeTexturePresenter
{
    /// <summary>
    /// Gets the shared presenter instance.
    /// </summary>
    public static TerminalShaderSkiaNativeTexturePresenter Instance { get; } = new();

    private TerminalShaderSkiaNativeTexturePresenter()
    {
    }

    /// <inheritdoc />
    public bool TryDraw(
        ISkiaSharpApiLease lease,
        SKCanvas destinationCanvas,
        TerminalShaderFrameResult result,
        SKRect destinationRect)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(destinationCanvas);
        ArgumentNullException.ThrowIfNull(result);

        TerminalShaderNativeTexture? texture = result.NativeTexture;
        if (texture is null || !texture.IsDrawable || lease.GrContext is null)
        {
            return false;
        }

        if (!TryGetSkiaFormat(texture, out SKColorType colorType, out uint backendFormat))
        {
            return false;
        }

        using GRBackendTexture? backendTexture = CreateBackendTexture(texture, backendFormat);
        if (backendTexture is null || !backendTexture.IsValid)
        {
            return false;
        }

        GRSurfaceOrigin origin = texture.Origin == TerminalShaderTextureOrigin.BottomLeft
            ? GRSurfaceOrigin.BottomLeft
            : GRSurfaceOrigin.TopLeft;

        try
        {
            using SKImage? image = SKImage.FromTexture(
                lease.GrContext,
                backendTexture,
                origin,
                colorType,
                SKAlphaType.Premul);
            if (image is null)
            {
                return false;
            }

            destinationCanvas.DrawImage(image, destinationRect);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static GRBackendTexture? CreateBackendTexture(
        TerminalShaderNativeTexture texture,
        uint backendFormat)
    {
        return texture.BackendKind switch
        {
            TerminalShaderBackendKind.Metal => CreateMetalTexture(texture),
            TerminalShaderBackendKind.Vulkan => CreateVulkanTexture(texture, backendFormat),
            TerminalShaderBackendKind.D3D12 => CreateD3DTexture(texture, backendFormat),
            _ => null,
        };
    }

    private static GRBackendTexture CreateMetalTexture(TerminalShaderNativeTexture texture)
    {
        return new GRBackendTexture(
            texture.Width,
            texture.Height,
            false,
            new GRMtlTextureInfo(texture.TextureHandle));
    }

    private static GRBackendTexture CreateVulkanTexture(
        TerminalShaderNativeTexture texture,
        uint backendFormat)
    {
        GRVkImageInfo imageInfo = new()
        {
            Image = ToUInt64(texture.TextureHandle),
            Format = backendFormat,
            ImageTiling = texture.ImageTiling,
            ImageLayout = texture.ImageLayout,
            ImageUsageFlags = texture.ImageUsageFlags,
            CurrentQueueFamily = texture.CurrentQueueFamily,
            SharingMode = texture.SharingMode,
            SampleCount = (uint)texture.SampleCount,
            LevelCount = (uint)texture.LevelCount,
        };

        if (texture.AllocationMemoryHandle != 0 ||
            texture.AllocationOffset != 0 ||
            texture.AllocationSize != 0 ||
            texture.AllocationFlags != 0 ||
            texture.AllocationBackendMemory != 0)
        {
            imageInfo.Alloc = new GRVkAlloc
            {
                Memory = ToUInt64(texture.AllocationMemoryHandle),
                Offset = texture.AllocationOffset,
                Size = texture.AllocationSize,
                Flags = texture.AllocationFlags,
                BackendMemory = ToNativeInt(texture.AllocationBackendMemory),
            };
        }

        return new GRBackendTexture(texture.Width, texture.Height, imageInfo);
    }

    private static GRBackendTexture CreateD3DTexture(
        TerminalShaderNativeTexture texture,
        uint backendFormat)
    {
        GRD3DTextureResourceInfo resourceInfo = new()
        {
            Resource = texture.TextureHandle,
            ResourceState = texture.ResourceState,
            Format = backendFormat,
            SampleCount = (uint)texture.SampleCount,
            LevelCount = (uint)texture.LevelCount,
        };

        return new GRBackendTexture(texture.Width, texture.Height, resourceInfo);
    }

    private static bool TryGetSkiaFormat(
        TerminalShaderNativeTexture texture,
        out SKColorType colorType,
        out uint backendFormat)
    {
        colorType = SKColorType.Unknown;
        backendFormat = texture.BackendFormat;
        switch (texture.Format)
        {
            case TerminalShaderNativeTextureFormat.Rgba8Unorm:
            case TerminalShaderNativeTextureFormat.Rgba8Srgb:
                colorType = SKColorType.Rgba8888;
                backendFormat = backendFormat == 0
                    ? GetDefaultRgba8BackendFormat(texture.BackendKind)
                    : backendFormat;
                return backendFormat != 0;

            case TerminalShaderNativeTextureFormat.Bgra8Unorm:
            case TerminalShaderNativeTextureFormat.Bgra8Srgb:
                colorType = SKColorType.Bgra8888;
                backendFormat = backendFormat == 0
                    ? GetDefaultBgra8BackendFormat(texture.BackendKind)
                    : backendFormat;
                return backendFormat != 0 || texture.BackendKind == TerminalShaderBackendKind.Metal;

            case TerminalShaderNativeTextureFormat.Rgba16Float:
                colorType = SKColorType.RgbaF16;
                backendFormat = backendFormat == 0
                    ? GetDefaultRgba16FloatBackendFormat(texture.BackendKind)
                    : backendFormat;
                return backendFormat != 0;

            default:
                return false;
        }
    }

    private static uint GetDefaultRgba8BackendFormat(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.D3D12 => 28,
            TerminalShaderBackendKind.Vulkan => 37,
            _ => 0,
        };
    }

    private static uint GetDefaultBgra8BackendFormat(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.D3D12 => 87,
            TerminalShaderBackendKind.Vulkan => 44,
            _ => 0,
        };
    }

    private static uint GetDefaultRgba16FloatBackendFormat(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.D3D12 => 10,
            TerminalShaderBackendKind.Vulkan => 97,
            _ => 0,
        };
    }

    private static ulong ToUInt64(nint handle)
    {
        return unchecked((ulong)handle.ToInt64());
    }

    private static nint ToNativeInt(ulong value)
    {
        return unchecked((nint)(long)value);
    }
}
