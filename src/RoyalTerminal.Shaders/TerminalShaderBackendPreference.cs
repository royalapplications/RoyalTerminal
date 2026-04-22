// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader backend selection.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes the preferred backend for compiler-backed full shader package execution.
/// </summary>
public enum TerminalShaderBackendPreference
{
    /// <summary>Select the platform default backend.</summary>
    Auto,

    /// <summary>Prefer Direct3D 11.</summary>
    D3D11,

    /// <summary>Prefer Direct3D 12.</summary>
    D3D12,

    /// <summary>Prefer Vulkan.</summary>
    Vulkan,

    /// <summary>Prefer Metal.</summary>
    Metal,
}
