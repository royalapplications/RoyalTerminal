// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes a native texture produced by a shader runtime.
/// </summary>
public sealed class TerminalShaderNativeTexture
{
    /// <summary>
    /// Initializes a native texture descriptor.
    /// </summary>
    /// <param name="backendKind">Backend that owns the texture.</param>
    /// <param name="textureHandle">Native texture or image handle.</param>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="format">Common texture pixel format.</param>
    /// <param name="origin">Texture coordinate origin.</param>
    /// <param name="sampleCount">Sample count.</param>
    /// <param name="levelCount">Mipmap level count.</param>
    /// <param name="textureViewHandle">Optional native texture view handle.</param>
    /// <param name="deviceHandle">Optional native device handle.</param>
    /// <param name="commandQueueHandle">Optional command queue handle.</param>
    /// <param name="commandBufferHandle">Optional command buffer/list handle.</param>
    /// <param name="resourceState">Backend resource state value.</param>
    /// <param name="backendFormat">Backend-specific format value.</param>
    /// <param name="imageTiling">Vulkan image tiling value.</param>
    /// <param name="imageLayout">Vulkan image layout value.</param>
    /// <param name="imageUsageFlags">Vulkan image usage flags.</param>
    /// <param name="currentQueueFamily">Vulkan current queue family index.</param>
    /// <param name="sharingMode">Vulkan sharing mode value.</param>
    /// <param name="allocationMemoryHandle">Optional Vulkan memory handle.</param>
    /// <param name="allocationOffset">Optional Vulkan allocation offset.</param>
    /// <param name="allocationSize">Optional Vulkan allocation size.</param>
    /// <param name="allocationFlags">Optional Vulkan allocation flags.</param>
    /// <param name="allocationBackendMemory">Optional Vulkan backend memory value.</param>
    public TerminalShaderNativeTexture(
        TerminalShaderBackendKind backendKind,
        nint textureHandle,
        int width,
        int height,
        TerminalShaderNativeTextureFormat format = TerminalShaderNativeTextureFormat.Rgba8Unorm,
        TerminalShaderTextureOrigin origin = TerminalShaderTextureOrigin.TopLeft,
        int sampleCount = 1,
        int levelCount = 1,
        nint textureViewHandle = 0,
        nint deviceHandle = 0,
        nint commandQueueHandle = 0,
        nint commandBufferHandle = 0,
        uint resourceState = 0,
        uint backendFormat = 0,
        uint imageTiling = 0,
        uint imageLayout = 0,
        uint imageUsageFlags = 0,
        uint currentQueueFamily = 0,
        uint sharingMode = 0,
        nint allocationMemoryHandle = 0,
        ulong allocationOffset = 0,
        ulong allocationSize = 0,
        uint allocationFlags = 0,
        ulong allocationBackendMemory = 0)
    {
        BackendKind = backendKind;
        TextureHandle = textureHandle;
        TextureViewHandle = textureViewHandle;
        DeviceHandle = deviceHandle;
        CommandQueueHandle = commandQueueHandle;
        CommandBufferHandle = commandBufferHandle;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Format = format;
        Origin = origin;
        SampleCount = Math.Max(1, sampleCount);
        LevelCount = Math.Max(1, levelCount);
        ResourceState = resourceState;
        BackendFormat = backendFormat;
        ImageTiling = imageTiling;
        ImageLayout = imageLayout;
        ImageUsageFlags = imageUsageFlags;
        CurrentQueueFamily = currentQueueFamily;
        SharingMode = sharingMode;
        AllocationMemoryHandle = allocationMemoryHandle;
        AllocationOffset = allocationOffset;
        AllocationSize = allocationSize;
        AllocationFlags = allocationFlags;
        AllocationBackendMemory = allocationBackendMemory;
    }

    /// <summary>
    /// Gets the backend that owns the texture.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the native texture or image handle.
    /// </summary>
    public nint TextureHandle { get; }

    /// <summary>
    /// Gets the optional native texture view handle.
    /// </summary>
    public nint TextureViewHandle { get; }

    /// <summary>
    /// Gets the optional native device handle.
    /// </summary>
    public nint DeviceHandle { get; }

    /// <summary>
    /// Gets the optional native command queue handle.
    /// </summary>
    public nint CommandQueueHandle { get; }

    /// <summary>
    /// Gets the optional native command buffer/list handle.
    /// </summary>
    public nint CommandBufferHandle { get; }

    /// <summary>
    /// Gets the texture width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the texture height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the common texture pixel format.
    /// </summary>
    public TerminalShaderNativeTextureFormat Format { get; }

    /// <summary>
    /// Gets the texture coordinate origin.
    /// </summary>
    public TerminalShaderTextureOrigin Origin { get; }

    /// <summary>
    /// Gets the sample count.
    /// </summary>
    public int SampleCount { get; }

    /// <summary>
    /// Gets the mipmap level count.
    /// </summary>
    public int LevelCount { get; }

    /// <summary>
    /// Gets the backend resource state value.
    /// </summary>
    public uint ResourceState { get; }

    /// <summary>
    /// Gets the backend-specific texture format value.
    /// </summary>
    public uint BackendFormat { get; }

    /// <summary>
    /// Gets the Vulkan image tiling value.
    /// </summary>
    public uint ImageTiling { get; }

    /// <summary>
    /// Gets the Vulkan image layout value.
    /// </summary>
    public uint ImageLayout { get; }

    /// <summary>
    /// Gets the Vulkan image usage flags.
    /// </summary>
    public uint ImageUsageFlags { get; }

    /// <summary>
    /// Gets the Vulkan current queue family index.
    /// </summary>
    public uint CurrentQueueFamily { get; }

    /// <summary>
    /// Gets the Vulkan sharing mode value.
    /// </summary>
    public uint SharingMode { get; }

    /// <summary>
    /// Gets the optional Vulkan memory handle.
    /// </summary>
    public nint AllocationMemoryHandle { get; }

    /// <summary>
    /// Gets the optional Vulkan allocation offset.
    /// </summary>
    public ulong AllocationOffset { get; }

    /// <summary>
    /// Gets the optional Vulkan allocation size.
    /// </summary>
    public ulong AllocationSize { get; }

    /// <summary>
    /// Gets the optional Vulkan allocation flags.
    /// </summary>
    public uint AllocationFlags { get; }

    /// <summary>
    /// Gets the optional Vulkan backend memory value.
    /// </summary>
    public ulong AllocationBackendMemory { get; }

    /// <summary>
    /// Gets whether the descriptor has enough shape information to draw.
    /// </summary>
    public bool IsDrawable => TextureHandle != 0 && Width > 0 && Height > 0;
}
