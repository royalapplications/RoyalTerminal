// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop.Skia - Result model for Skia interop rendering.

using GhosttySharp.Rendering.Contracts;

namespace GhosttySharp.Rendering.Interop.Skia;

/// <summary>
/// Represents one Skia interop render pass outcome.
/// </summary>
public readonly record struct SkiaInteropRenderResult
{
    /// <summary>
    /// Initializes a render result.
    /// </summary>
    public SkiaInteropRenderResult(RenderFrameResult frameResult, bool usedCpuFallback)
    {
        FrameResult = frameResult;
        UsedCpuFallback = usedCpuFallback;
    }

    /// <summary>
    /// Gets the frame result metadata.
    /// </summary>
    public RenderFrameResult FrameResult { get; }

    /// <summary>
    /// Gets whether the CPU RGBA fallback path was used.
    /// </summary>
    public bool UsedCpuFallback { get; }
}
