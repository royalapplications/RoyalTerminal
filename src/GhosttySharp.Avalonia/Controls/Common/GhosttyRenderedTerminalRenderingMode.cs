// Licensed under the MIT License.
// GhosttySharp.Avalonia - Rendering mode selection for GhosttyRenderedTerminalControl.

namespace GhosttySharp.Avalonia.Controls;

/// <summary>
/// Selects the rendering path used by <see cref="GhosttyRenderedTerminalControl"/>.
/// </summary>
public enum GhosttyRenderedTerminalRenderingMode
{
    /// <summary>
    /// Existing rendered control path: Ghostty screen cell reads + Skia cell renderer.
    /// </summary>
    CpuCellRenderer = 0,

    /// <summary>
    /// New interop path: renderer C-API through Skia bridge with GPU target + CPU fallback.
    /// </summary>
    TextureInterop = 1,
}
