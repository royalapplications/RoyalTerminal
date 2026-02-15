// Licensed under the MIT License.
// RoyalTerminal.Rendering.Contracts - Render surface abstraction.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Represents a backend render surface that can render into external targets.
/// </summary>
public interface IRenderSurface : IDisposable
{
    /// <summary>
    /// Gets the backend kind used by this surface.
    /// </summary>
    RenderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets backend capabilities for this surface.
    /// </summary>
    RenderBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Updates surface size in pixels.
    /// </summary>
    void SetSize(int width, int height);

    /// <summary>
    /// Updates content scaling factors.
    /// </summary>
    void SetScale(double scaleX, double scaleY);

    /// <summary>
    /// Validates a target descriptor for this surface.
    /// </summary>
    RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor);

    /// <summary>
    /// Renders one frame into the supplied target.
    /// </summary>
    RenderFrameResult Render(in RenderTargetDescriptor descriptor);
}
