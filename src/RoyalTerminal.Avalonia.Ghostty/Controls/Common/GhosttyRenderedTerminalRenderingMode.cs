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
    /// Renderer C-API through Skia bridge with GPU target plus CPU RGBA fallback.
    /// </summary>
    TextureInterop = 0,
}
