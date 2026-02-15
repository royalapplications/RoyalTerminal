// Licensed under the MIT License.
// RoyalTerminal.Rendering.Contracts - Rendering capability flags.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Feature flags describing renderer backend capabilities.
/// </summary>
[Flags]
public enum RenderFeatureFlags
{
    /// <summary>No optional features are available.</summary>
    None = 0,

    /// <summary>Supports rendering directly to external textures.</summary>
    ExternalTextureTargets = 1 << 0,

    /// <summary>Supports rendering directly to external framebuffers.</summary>
    ExternalFramebufferTargets = 1 << 1,

    /// <summary>Supports explicit begin/end frame semantics.</summary>
    ExplicitFrameLifecycle = 1 << 2,

    /// <summary>Supports CPU fallback rendering to RGBA buffers.</summary>
    CpuRgbaFallback = 1 << 3,

    /// <summary>Requires explicit synchronization token handling.</summary>
    ExplicitSynchronization = 1 << 4,

    /// <summary>Supports partial-damage region rendering.</summary>
    PartialDamage = 1 << 5,
}
