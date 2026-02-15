// Licensed under the MIT License.
// GhosttySharp.Avalonia.Rendering - Preferred backend selector for interop target acquisition.

namespace GhosttySharp.Avalonia.Rendering.Interop;

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
}
