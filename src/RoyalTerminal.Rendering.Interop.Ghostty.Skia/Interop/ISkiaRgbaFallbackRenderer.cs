// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty.Skia - CPU fallback render contract.

using RoyalTerminal.Rendering.Contracts;

namespace RoyalTerminal.Rendering.Interop.Ghostty.Skia;

/// <summary>
/// Provides CPU RGBA fallback rendering for Skia interop adapters.
/// </summary>
public interface ISkiaRgbaFallbackRenderer
{
    /// <summary>
    /// Renders one frame to an RGBA destination span.
    /// </summary>
    /// <param name="destination">Destination RGBA bytes.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    /// <returns>Render result metadata.</returns>
    RenderFrameResult RenderToRgba(Span<byte> destination, int width, int height, int stride);
}
