// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies the kind of work performed by a shader package pass.
/// </summary>
public enum TerminalShaderStage
{
    /// <summary>
    /// A pixel shader pass that draws a full-frame target.
    /// </summary>
    Pixel,

    /// <summary>
    /// A compute shader pass dispatched over a thread-group grid.
    /// </summary>
    Compute,

    /// <summary>
    /// A backend copy or blit pass.
    /// </summary>
    Copy,

    /// <summary>
    /// A backend clear pass.
    /// </summary>
    Clear,
}
