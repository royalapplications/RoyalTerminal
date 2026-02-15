// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Contracts - Common render target pixel formats.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Common pixel formats for external render targets.
/// </summary>
public enum RenderPixelFormat
{
    /// <summary>Unknown format.</summary>
    Unknown = 0,

    /// <summary>8-bit BGRA UNORM.</summary>
    Bgra8Unorm = 1,

    /// <summary>8-bit BGRA sRGB.</summary>
    Bgra8Srgb = 2,

    /// <summary>8-bit RGBA UNORM.</summary>
    Rgba8Unorm = 3,

    /// <summary>8-bit RGBA sRGB.</summary>
    Rgba8Srgb = 4,

    /// <summary>16-bit RGBA float.</summary>
    Rgba16Float = 5,
}
