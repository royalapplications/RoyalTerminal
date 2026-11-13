// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyImageLayerFilterTests
{
    // libghostty's iterator consumes a uint32 mask, whereas render-state layer
    // values are ordinal enum values. Follow kitty_graphics.h and its upstream
    // placement_iterator_set layer-filter tests, including z-index boundaries.
    [GhosttyNativeFact]
    public void SingleLayerSelectsOnlyItsOwnPlacements()
    {
        using GhosttyTerminal terminal = CreateTerminal();
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        using GhosttyKittyGraphicsPlacementIterator iterator = new();
        foreach ((GhosttyImageLayer layer, uint expected) in new[]
        {
            (GhosttyImageLayer.BelowBackground, 1u << 3),
            (GhosttyImageLayer.BelowText, 1u << 2),
            (GhosttyImageLayer.AboveText, 1u << 1),
        })
        {
            iterator.SetLayer(layer);
            Assert.Equal(expected, ReadPlacements(graphics!, iterator));
        }
    }

    [GhosttyNativeFact]
    public void MasksSelectEveryCombinationAndZeroResetsTheFilter()
    {
        using GhosttyTerminal terminal = CreateTerminal();
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        using GhosttyKittyGraphicsPlacementIterator iterator = new();
        uint[] expected = [14, 8, 4, 12, 2, 10, 6, 14];
        for (uint mask = 0; mask < expected.Length; mask++)
        {
            iterator.SetLayers((GhosttyImageLayerMask)mask);
            Assert.Equal(expected[mask], ReadPlacements(graphics!, iterator));
        }
        iterator.SetLayer(GhosttyImageLayer.BelowBackground);
        iterator.SetLayers(0);
        Assert.Equal(14u, ReadPlacements(graphics!, iterator));
    }

    [GhosttyNativeFact]
    public void InvalidLayersPreserveThePreviousFilterAndDisposedIteratorsRejectChanges()
    {
        using GhosttyTerminal terminal = CreateTerminal();
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        using GhosttyKittyGraphicsPlacementIterator iterator = new();
        iterator.SetLayer(GhosttyImageLayer.AboveText);
        Assert.Throws<InvalidOperationException>(() => iterator.SetLayers((GhosttyImageLayerMask)8));
        Assert.Throws<ArgumentOutOfRangeException>(() => iterator.SetLayer((GhosttyImageLayer)32));
        Assert.Throws<ArgumentOutOfRangeException>(() => iterator.SetLayer((GhosttyImageLayer)(-1)));
        Assert.Equal(2u, ReadPlacements(graphics!, iterator));
        iterator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => iterator.SetLayer(GhosttyImageLayer.AboveText));
        Assert.Throws<ObjectDisposedException>(() => iterator.SetLayers(GhosttyImageLayerMask.All));
    }

    private static GhosttyTerminal CreateTerminal()
    {
        GhosttyTerminal terminal = new(80, 24);
        terminal.Write("\u001b_Ga=t,t=d,f=24,i=1,s=1,v=2;////////\u001b\\"u8);
        terminal.Write("\u001b_Ga=p,i=1,p=1,z=0;\u001b\\"u8);
        terminal.Write("\u001b_Ga=p,i=1,p=2,z=-1073741824;\u001b\\"u8);
        terminal.Write("\u001b_Ga=p,i=1,p=3,z=-1073741825;\u001b\\"u8);
        return terminal;
    }

    private static uint ReadPlacements(GhosttyKittyGraphics graphics, GhosttyKittyGraphicsPlacementIterator iterator)
    {
        graphics.Populate(iterator);
        uint result = 0;
        while (iterator.MoveNext()) result |= 1u << checked((int)iterator.GetPlacementId());
        return result;
    }
}
