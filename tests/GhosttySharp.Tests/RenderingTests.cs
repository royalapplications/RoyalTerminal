// Licensed under the MIT License.
// GhosttySharp.Tests — GlyphCache and SkiaTerminalRenderer tests.

using GhosttySharp.Avalonia.Rendering;
using Xunit;

namespace GhosttySharp.Tests;

/// <summary>
/// Tests for the rendering infrastructure.
/// </summary>
public class RenderingTests
{
    [Fact]
    public void GlyphCache_CanBeCreated()
    {
        using var cache = new GlyphCache("Consolas");
        Assert.NotNull(cache);
    }

    [Fact]
    public void GlyphCache_HasTypeface()
    {
        using var cache = new GlyphCache("Consolas");
        Assert.NotNull(cache.RegularTypeface);
    }

    [Fact]
    public void GlyphCache_Dispose_CanBeCalledMultipleTimes()
    {
        var cache = new GlyphCache("Consolas");
        cache.Dispose();
        cache.Dispose(); // Should not throw
    }

    [Fact]
    public void SkiaTerminalRenderer_CanBeCreated()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.NotNull(renderer);
    }

    [Fact]
    public void SkiaTerminalRenderer_CellDimensions_ArePositive()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.True(renderer.CellWidth > 0);
        Assert.True(renderer.CellHeight > 0);
    }

    [Fact]
    public void SkiaTerminalRenderer_CursorVisible_DefaultTrue()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.True(renderer.CursorVisible);
    }

    [Fact]
    public void SkiaTerminalRenderer_CursorVisible_CanBeSet()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.CursorVisible = false;
        Assert.False(renderer.CursorVisible);
    }

    [Fact]
    public void SkiaTerminalRenderer_Selection_InitiallyNull()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        Assert.Null(renderer.SelectionStart);
        Assert.Null(renderer.SelectionEnd);
    }

    [Fact]
    public void SkiaTerminalRenderer_Selection_CanBeSet()
    {
        var renderer = new SkiaTerminalRenderer("Consolas", 14f);
        renderer.SelectionStart = (0, 0);
        renderer.SelectionEnd = (10, 5);

        Assert.Equal((0, 0), renderer.SelectionStart);
        Assert.Equal((10, 5), renderer.SelectionEnd);
    }
}
