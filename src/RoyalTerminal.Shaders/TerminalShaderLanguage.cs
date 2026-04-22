// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Terminal shader source languages.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Identifies the source language used for a terminal framebuffer shader.
/// </summary>
public enum TerminalShaderLanguage
{
    /// <summary>
    /// Skia Runtime Effect source. This is the canonical runtime format.
    /// </summary>
    SkiaRuntimeEffect,

    /// <summary>
    /// Ghostty/Shadertoy-style GLSL source with a <c>mainImage</c> entry point.
    /// </summary>
    GhosttyShadertoy,

    /// <summary>
    /// Windows Terminal-style HLSL source with a terminal texture and
    /// <c>PixelShaderSettings</c> constant buffer.
    /// </summary>
    WindowsTerminalHlsl,
}
