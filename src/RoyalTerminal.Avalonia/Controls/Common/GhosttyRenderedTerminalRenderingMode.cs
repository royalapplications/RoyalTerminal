// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Rendering mode selection for GhosttyRenderedTerminalControl.

namespace RoyalTerminal.Avalonia.Controls;

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
