// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader runtime model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Describes capabilities exposed by a full shader runtime backend.
/// </summary>
public sealed class TerminalShaderBackendCapabilities
{
    /// <summary>
    /// Initializes new shader backend capabilities.
    /// </summary>
    /// <param name="backendKind">Backend kind.</param>
    /// <param name="supportsPixelShaders">Whether pixel shader passes are supported.</param>
    /// <param name="supportsComputeShaders">Whether compute shader passes are supported.</param>
    /// <param name="supportsUavResources">Whether UAV resources are supported.</param>
    /// <param name="supportsTextureInterop">Whether final textures can be exported for composition interop.</param>
    /// <param name="maxTextureSize">Maximum texture dimension supported by the backend.</param>
    public TerminalShaderBackendCapabilities(
        TerminalShaderBackendKind backendKind,
        bool supportsPixelShaders,
        bool supportsComputeShaders,
        bool supportsUavResources,
        bool supportsTextureInterop,
        int maxTextureSize)
    {
        BackendKind = backendKind;
        SupportsPixelShaders = supportsPixelShaders;
        SupportsComputeShaders = supportsComputeShaders;
        SupportsUavResources = supportsUavResources;
        SupportsTextureInterop = supportsTextureInterop;
        MaxTextureSize = Math.Max(1, maxTextureSize);
    }

    /// <summary>
    /// Gets the backend kind.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets whether pixel shader passes are supported.
    /// </summary>
    public bool SupportsPixelShaders { get; }

    /// <summary>
    /// Gets whether compute shader passes are supported.
    /// </summary>
    public bool SupportsComputeShaders { get; }

    /// <summary>
    /// Gets whether UAV resources are supported.
    /// </summary>
    public bool SupportsUavResources { get; }

    /// <summary>
    /// Gets whether final textures can be exported for composition interop.
    /// </summary>
    public bool SupportsTextureInterop { get; }

    /// <summary>
    /// Gets the maximum texture dimension supported by the backend.
    /// </summary>
    public int MaxTextureSize { get; }
}
