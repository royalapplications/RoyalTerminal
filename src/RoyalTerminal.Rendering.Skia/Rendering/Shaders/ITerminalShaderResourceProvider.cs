// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader runtime model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Supplies runtime resources for full shader package execution.
/// </summary>
public interface ITerminalShaderResourceProvider
{
    /// <summary>
    /// Attempts to resolve a runtime shader resource.
    /// </summary>
    /// <param name="resourceName">Resource name requested by the package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved resource, or <c>null</c> when unavailable.</returns>
    ValueTask<TerminalShaderResourceValue?> TryGetResourceAsync(
        string resourceName,
        CancellationToken cancellationToken = default);
}
