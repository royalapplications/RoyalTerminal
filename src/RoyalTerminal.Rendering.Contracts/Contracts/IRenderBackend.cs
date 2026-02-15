// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
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
