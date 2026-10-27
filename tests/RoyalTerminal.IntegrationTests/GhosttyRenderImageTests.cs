// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyRenderImageTests
{
    [GhosttyNativeFact]
    public void FixedAndRelativeImagesUseUnifiedGeometryAndGeneration()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(20, 6);
        terminal.Resize(20, 6, 8, 16);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\x1b_Ga=T,f=32,i=1,p=7,s=1,v=1,c=2,r=2,C=1;/wAA/w==\x1b\\"u8);
        terminal.Write("\x1b_Ga=T,f=32,i=2,p=9,s=1,v=1,c=1,r=1,P=1,Q=7,H=3,V=2,C=1;AAD//w==\x1b\\"u8);
        using GhosttyRenderState state = new();
        state.Update(terminal);
        List<GhosttyRenderImagePlacement> placements = Read(state);
        Assert.Equal(2, placements.Count);
        GhosttyRenderImagePlacement parent = Assert.Single(placements, p => p.ImageId == 1);
        GhosttyRenderImagePlacement child = Assert.Single(placements, p => p.ImageId == 2);
        Assert.Equal(GhosttyImageProtocol.Kitty, parent.Protocol);
        Assert.Equal(GhosttyRenderStateKittyPlacementKind.Fixed, parent.KittyKind);
        Assert.Equal(GhosttyRenderStateKittyPlacementKind.Relative, child.KittyKind);
        Assert.Equal((uint?)7, parent.KittyPlacementId);
        Assert.Equal((uint?)9, child.KittyPlacementId);
        Assert.Equal((16u, 32u, 2u, 2u), (parent.PixelWidth, parent.PixelHeight, parent.GridColumns, parent.GridRows));
        Assert.Equal((0u, 0u, 1u, 1u), (parent.SourceX, parent.SourceY, parent.SourceWidth, parent.SourceHeight));
        Assert.Equal((3, 2), (child.ViewportColumn, child.ViewportRow));
        Assert.Equal(GhosttyRenderStateImageLayer.AboveText, parent.Layer);
        ulong generation = state.GetImagePlacementsGeneration();
        state.Clean();
        terminal.Write("\x1b[6;1Hplain text"u8);
        state.Update(terminal);
        Assert.Equal(generation, state.GetImagePlacementsGeneration());
        Assert.Equal(parent.ImageGeneration, Assert.Single(Read(state), p => p.ImageId == 1).ImageGeneration);
        terminal.Write("\x1b_Ga=d,d=I,i=2\x1b\\"u8);
        state.Update(terminal);
        Assert.NotEqual(generation, state.GetImagePlacementsGeneration());
        Assert.Equal(1u, Assert.Single(Read(state)).ImageId);
    }

    [GhosttyNativeFact]
    public void ImagesWithoutCellDimensionsAreEmptyAndDisposedStateRejectsQueries()
    {
        using GhosttyTerminal terminal = new(10, 3);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\x1b_Ga=T,i=1,s=1,v=1,C=1;/wAA/w==\x1b\\"u8);
        GhosttyRenderState state = new();
        state.Update(terminal);
        Assert.Empty(Read(state));
        state.Dispose();
        state.Dispose();
        Assert.Throws<ObjectDisposedException>(() => state.ResetImageIterator());
        Assert.Throws<ObjectDisposedException>(() => state.MoveNextImage());
        Assert.Throws<ObjectDisposedException>(() => state.GetCurrentImage());
        Assert.Throws<ObjectDisposedException>(() => state.GetImagePlacementsGeneration());
    }

    private static List<GhosttyRenderImagePlacement> Read(GhosttyRenderState state)
    {
        List<GhosttyRenderImagePlacement> result = [];
        state.ResetImageIterator();
        while (state.MoveNextImage()) result.Add(state.GetCurrentImage());
        return result;
    }
}
