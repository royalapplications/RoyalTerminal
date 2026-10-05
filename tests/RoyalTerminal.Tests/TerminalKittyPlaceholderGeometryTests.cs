// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalKittyPlaceholderGeometryTests
{
    // Exact expected values from Ghostty graphics_unicode.zig's dog-image tests.
    [Theory]
    [InlineData(4, 2, 0, 36, 0, 153, 144, 44)]
    [InlineData(4, 2, 1, 0, 153, 153, 144, 44)]
    [InlineData(2, 2, 0, 58, 0, 153, 72, 22)]
    [InlineData(2, 2, 1, 0, 153, 153, 72, 22)]
    [InlineData(1, 1, 0, 29, 0, 306, 36, 22)]
    public void MatchesUpstreamGoldenLetterboxFragments(uint columns, uint rows, uint imageRow,
        uint offsetY, uint sourceY, uint sourceHeight, uint width, uint height)
    {
        TerminalKittyPlaceholderRun run = new(0, 1, 0, 0, imageRow, 4);
        Assert.True(TerminalKittyPlaceholderGeometry.TryProject(run, 500, 306, columns, rows, 36, 80, out var geometry));
        Assert.Equal(new(0, offsetY, 0, sourceY, 500, sourceHeight, width, height), geometry);
    }

    [Fact]
    public void PillarboxPaddingAndFragmentsAreClipped()
    {
        TerminalKittyPlaceholderRun run = new(0, 1, 0, 0, 0, 1);
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 4, 1, 10, 20, out _));
        run = run with { ImageColumn = 1 };
        Assert.True(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 4, 1, 10, 20, out var left));
        Assert.Equal(new(5, 0, 0, 0, 5, 20, 5, 20), left);
        run = run with { ImageColumn = 2 };
        Assert.True(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 4, 1, 10, 20, out var right));
        Assert.Equal(new(0, 0, 5, 0, 5, 20, 5, 20), right);
        run = run with { ImageColumn = 3 };
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 4, 1, 10, 20, out _));
    }

    [Fact]
    public void UnspecifiedGridUsesCeilingOfEachOriginalImageDimension()
    {
        TerminalKittyPlaceholderRun run = new(0, 1, 0, 0, 0, 1);
        Assert.True(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 0, 0, 10, 20, out var geometry));
        Assert.Equal(new(0, 0, 0, 0, 10, 20, 10, 20), geometry);
        Assert.True(TerminalKittyPlaceholderGeometry.TryProject(run, 11, 21, 0, 0, 10, 20, out var roundedGrid));
        Assert.True(TerminalKittyPlaceholderGeometry.TryProject(run, 11, 21, 2, 2, 10, 20, out var explicitGrid));
        Assert.Equal(explicitGrid, roundedGrid);
    }

    [Fact]
    public void RejectsZeroMetricsAndUnrepresentableGridsWithoutOverflow()
    {
        TerminalKittyPlaceholderRun run = new(0, 1, 0, 0, 0, 1);
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 1, 1, 0, 20, out _));
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 65536, 1, 10, 20, out _));
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run, uint.MaxValue, 20, 0, 0, 1, 20, out _));
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run, 10, 20, 2, 2, uint.MaxValue, 20, out _));
        Assert.False(TerminalKittyPlaceholderGeometry.TryProject(run with { ImageColumn = uint.MaxValue }, 10, 20, 1, 1, 10, 20, out _));
    }
}
