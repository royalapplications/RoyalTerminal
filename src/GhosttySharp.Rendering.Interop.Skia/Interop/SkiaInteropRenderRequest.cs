// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop.Skia - Request model for Skia interop rendering.

using GhosttySharp.Rendering.Contracts;
using SkiaSharp;

namespace GhosttySharp.Rendering.Interop.Skia;

/// <summary>
/// Describes one Skia bridge render request.
/// </summary>
public readonly record struct SkiaInteropRenderRequest
{
    /// <summary>
    /// Initializes a request with default fallback behavior enabled.
    /// </summary>
    public SkiaInteropRenderRequest()
    {
        AllowCpuFallback = true;
    }

    /// <summary>
    /// Gets the native render target descriptor.
    /// </summary>
    public required RenderTargetDescriptor TargetDescriptor { get; init; }

    /// <summary>
    /// Gets whether CPU RGBA fallback is allowed when direct interop is unavailable.
    /// </summary>
    public bool AllowCpuFallback { get; init; }

    /// <summary>
    /// Gets an optional destination rectangle for fallback bitmap draws.
    /// </summary>
    public SKRect? DestinationRect { get; init; }

    /// <summary>
    /// Gets whether fallback draw should clear the canvas first.
    /// </summary>
    public bool ClearCanvasBeforeFallbackDraw { get; init; }

    /// <summary>
    /// Gets the clear color used when <see cref="ClearCanvasBeforeFallbackDraw"/> is true.
    /// </summary>
    public SKColor ClearColor { get; init; }
}
