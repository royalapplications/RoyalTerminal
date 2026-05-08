// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Composition draw handler regression tests.

using Avalonia;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Verifies that viewport target sizing stays separate from dirty update clips,
/// matching reference terminal renderers that treat clips as repaint regions.
/// </summary>
public sealed class TerminalDrawHandlerTests
{
    [Fact]
    public void RenderTargetPixelSize_UsesRenderBounds_WhenClipIsPartial()
    {
        Rect renderBounds = new(0, 0, 960, 600);
        SKRect partialClip = new(0, 0, 960, 280);

        (int width, int height) = TerminalDrawHandler.GetRenderTargetPixelSize(renderBounds, partialClip);

        Assert.Equal(960, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void RenderTargetPixelSize_FallsBackToClip_WhenRenderBoundsAreUnavailable()
    {
        Rect renderBounds = default;
        SKRect clip = new(0, 0, 640, 360);

        (int width, int height) = TerminalDrawHandler.GetRenderTargetPixelSize(renderBounds, clip);

        Assert.Equal(640, width);
        Assert.Equal(360, height);
    }
}
