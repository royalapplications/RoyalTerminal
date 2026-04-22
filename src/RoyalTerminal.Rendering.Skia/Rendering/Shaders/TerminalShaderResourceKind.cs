// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies the kind of resource required by a full shader package.
/// </summary>
public enum TerminalShaderResourceKind
{
    /// <summary>Rendered terminal framebuffer texture.</summary>
    TerminalFramebuffer,

    /// <summary>Read-only two-dimensional texture.</summary>
    Texture2D,

    /// <summary>Sampler state.</summary>
    Sampler,

    /// <summary>Constant buffer.</summary>
    ConstantBuffer,

    /// <summary>Structured buffer.</summary>
    StructuredBuffer,

    /// <summary>Byte-address buffer.</summary>
    ByteAddressBuffer,

    /// <summary>Writable two-dimensional texture.</summary>
    UavTexture2D,

    /// <summary>Writable buffer.</summary>
    UavBuffer,

    /// <summary>Pass render target attachment.</summary>
    RenderTarget,

    /// <summary>Frame-history texture.</summary>
    HistoryTexture,
}
