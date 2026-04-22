// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader compiler model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies the compiled shader code format.
/// </summary>
public enum TerminalShaderCompiledCodeFormat
{
    /// <summary>Unknown compiled format.</summary>
    Unknown,

    /// <summary>DirectX Intermediate Language.</summary>
    Dxil,

    /// <summary>Direct3D bytecode.</summary>
    Dxbc,

    /// <summary>Standard Portable Intermediate Representation for Vulkan.</summary>
    SpirV,

    /// <summary>Metal Shading Language source.</summary>
    Msl,

    /// <summary>Skia Runtime Effect source.</summary>
    SkiaRuntimeEffectSource,
}
