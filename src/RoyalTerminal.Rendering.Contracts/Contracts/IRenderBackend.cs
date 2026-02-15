// Licensed under the MIT License.
// RoyalTerminal.Rendering.Contracts - Rendering backend abstraction.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Represents a rendering backend implementation contract.
/// </summary>
public interface IRenderBackend
{
    /// <summary>
    /// Gets the backend kind.
    /// </summary>
    RenderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets backend capabilities.
    /// </summary>
    RenderBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Validates an external target descriptor against backend requirements.
    /// </summary>
    RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor);
}
