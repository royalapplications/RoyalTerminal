// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Identifies the coordinate origin of a native texture.
/// </summary>
public enum TerminalShaderTextureOrigin
{
    /// <summary>Texture coordinates begin at the top-left corner.</summary>
    TopLeft = 0,

    /// <summary>Texture coordinates begin at the bottom-left corner.</summary>
    BottomLeft = 1,
}
