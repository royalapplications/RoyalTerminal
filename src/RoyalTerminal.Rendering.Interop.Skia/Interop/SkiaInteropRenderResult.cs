// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Skia - Result model for Skia interop rendering.

using RoyalTerminal.Rendering.Contracts;

namespace RoyalTerminal.Rendering.Interop.Skia;

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
