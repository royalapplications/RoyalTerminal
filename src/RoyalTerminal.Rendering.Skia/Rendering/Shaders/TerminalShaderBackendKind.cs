// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader compiler model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies a full shader execution backend.
/// </summary>
public enum TerminalShaderBackendKind
{
    /// <summary>Skia Runtime Effect backend.</summary>
    SkiaRuntimeEffect,

    /// <summary>Direct3D 11 backend.</summary>
    D3D11,

    /// <summary>Direct3D 12 backend.</summary>
    D3D12,

    /// <summary>Vulkan backend.</summary>
    Vulkan,

    /// <summary>Metal backend.</summary>
    Metal,
}
