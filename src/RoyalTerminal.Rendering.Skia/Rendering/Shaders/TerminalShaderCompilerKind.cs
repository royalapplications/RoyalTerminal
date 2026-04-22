// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader compiler model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies the compiler used for a full shader package.
/// </summary>
public enum TerminalShaderCompilerKind
{
    /// <summary>Use the runtime's default compiler for the selected backend.</summary>
    Auto,

    /// <summary>Use the Microsoft DirectX Shader Compiler.</summary>
    Dxc,

    /// <summary>Use the Slang compiler.</summary>
    Slang,
}
