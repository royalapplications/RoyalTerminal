// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Specifies the rendering backend options for the terminal controls.
/// </summary>
public enum TerminalRendererType
{
    /// <summary>
    /// Managed rendering path using SkiaSharp to draw into a visual bitmap.
    /// </summary>
    Skia = 0,

    /// <summary>
    /// Native Ghostty interop rendering via texture sharing.
    /// </summary>
    Ghostty = 1,

    /// <summary>
    /// Native Swift/Metal engine rendering directly to shared textures.
    /// </summary>
    SwiftMetal = 2
}
