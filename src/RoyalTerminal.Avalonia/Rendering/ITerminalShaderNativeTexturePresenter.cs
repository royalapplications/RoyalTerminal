// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Shader native texture presentation boundary.

using Avalonia.Skia;
using RoyalTerminal.Shaders;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Draws native textures produced by full shader package runtimes.
/// </summary>
public interface ITerminalShaderNativeTexturePresenter
{
    /// <summary>
    /// Attempts to draw a native texture frame result into the destination canvas.
    /// </summary>
    /// <param name="lease">Current Avalonia Skia API lease.</param>
    /// <param name="destinationCanvas">Destination Skia canvas.</param>
    /// <param name="result">Shader frame result.</param>
    /// <param name="destinationRect">Destination rectangle.</param>
    /// <returns><see langword="true"/> when the native texture was drawn.</returns>
    bool TryDraw(
        ISkiaSharpApiLease lease,
        SKCanvas destinationCanvas,
        TerminalShaderFrameResult result,
        SKRect destinationRect);
}
