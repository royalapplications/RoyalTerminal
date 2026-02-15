// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Contracts - Rendering backend identifiers.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Identifies the GPU backend used by a renderer.
/// </summary>
public enum RenderBackendKind
{
    /// <summary>Unknown or uninitialized backend.</summary>
    Unknown = 0,

    /// <summary>Software-only rendering path.</summary>
    Software = 1,

    /// <summary>Apple Metal backend.</summary>
    Metal = 2,

    /// <summary>Vulkan backend.</summary>
    Vulkan = 3,

    /// <summary>Direct3D 11 backend.</summary>
    D3D11 = 4,

    /// <summary>Direct3D 12 backend.</summary>
    D3D12 = 5,

    /// <summary>OpenGL backend.</summary>
    OpenGL = 6,
}
