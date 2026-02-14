// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop.Skia - CPU RGBA fallback adapter for GhosttyRenderSurface.

using GhosttySharp.Rendering.Contracts;
using GhosttySharp.Rendering.Interop;

namespace GhosttySharp.Rendering.Interop.Skia;

/// <summary>
/// Adapts <see cref="GhosttyRenderSurface"/> to <see cref="ISkiaRgbaFallbackRenderer"/>.
/// </summary>
public sealed class GhosttyRenderSurfaceRgbaFallbackRenderer : ISkiaRgbaFallbackRenderer
{
    private readonly GhosttyRenderSurface _surface;

    /// <summary>
    /// Initializes a new fallback adapter.
    /// </summary>
    public GhosttyRenderSurfaceRgbaFallbackRenderer(GhosttyRenderSurface surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    /// <inheritdoc />
    public RenderFrameResult RenderToRgba(Span<byte> destination, int width, int height, int stride)
    {
        return _surface.RenderToRgba(destination, width, height, stride);
    }
}
