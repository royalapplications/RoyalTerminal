// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader built-in resources.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Defines stable names for resources supplied by the terminal renderer.
/// </summary>
public static class TerminalShaderBuiltInResourceNames
{
    /// <summary>
    /// The current terminal framebuffer resource.
    /// </summary>
    public const string TerminalFramebuffer = "TerminalFramebuffer";

    /// <summary>
    /// Compatibility alias used by simple framebuffer shader samples.
    /// </summary>
    public const string ShaderTexture = "shaderTexture";

    /// <summary>
    /// Compatibility alias used by Shadertoy-style samples.
    /// </summary>
    public const string Channel0 = "iChannel0";
}
