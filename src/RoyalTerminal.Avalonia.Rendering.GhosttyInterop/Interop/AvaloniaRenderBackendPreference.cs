// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Preferred backend selector for interop target acquisition.

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

/// <summary>
/// Defines preferred backend selection behavior for Avalonia interop rendering.
/// </summary>
public enum AvaloniaRenderBackendPreference
{
    /// <summary>
    /// Select backend from host platform defaults.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Prefer Metal interop.
    /// </summary>
    Metal = 1,

    /// <summary>
    /// Prefer Vulkan interop.
    /// </summary>
    Vulkan = 2,

    /// <summary>
    /// Prefer Direct3D 11 interop.
    /// </summary>
    D3D11 = 3,

    /// <summary>
    /// Prefer Direct3D 12 interop.
    /// </summary>
    D3D12 = 4,

    /// <summary>
    /// Force software descriptor fallback.
    /// </summary>
    Software = 5,

    /// <summary>
    /// Prefer OpenGL interop.
    /// </summary>
    OpenGL = 6,
}
