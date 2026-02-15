// Licensed under the MIT License.
// RoyalTerminal.Rendering.Contracts - Render target kinds.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Describes the destination target type used for a render pass.
/// </summary>
public enum RenderTargetKind
{
    /// <summary>Unknown or uninitialized target kind.</summary>
    Unknown = 0,

    /// <summary>Texture 2D render target.</summary>
    Texture2D = 1,

    /// <summary>Framebuffer render target.</summary>
    Framebuffer = 2,
}
