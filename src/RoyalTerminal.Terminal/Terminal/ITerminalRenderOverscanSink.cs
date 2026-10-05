// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>Optional adapter capability for mirroring off-viewport render rows.</summary>
/// <remarks>
/// Callers serialize requests with input, rendering and resize using the screen lock.
/// Managed screens already own their history and do not need this capability.
/// This does not enable smooth scrolling or extend image-scene/cursor coordinates.
/// </remarks>
public interface ITerminalRenderOverscanSink
{
    /// <summary>
    /// Gets or sets the persistent extra-row request. Zero is the default. Changes
    /// refresh the mirror unless synchronized output is holding presentation, in
    /// which case the last completed capture remains visible until publication.
    /// Actual accessible counts are available from <see cref="TerminalScreen.GetRenderViewport"/>.
    /// </summary>
    TerminalRenderOverscan RenderOverscan { get; set; }
}
