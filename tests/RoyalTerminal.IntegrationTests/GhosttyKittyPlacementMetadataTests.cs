// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyKittyPlacementMetadataTests
{
    [Fact]
    public unsafe void MetadataLayoutMatchesNativeHeader()
    {
        GhosttyVtNative.RoyalKittyPlacementMetadata metadata = GhosttyVtNative.RoyalKittyPlacementMetadata.CreateSized();
        Assert.Equal((nuint)40, metadata.Size);
        Assert.Equal(40, Unsafe.SizeOf<GhosttyVtNative.RoyalKittyPlacementMetadata>());
        Assert.Equal(8, (byte*)&metadata.ImageId - (byte*)&metadata);
        Assert.Equal(16, (byte*)&metadata.Flags - (byte*)&metadata);
        Assert.Equal(32, (byte*)&metadata.HorizontalOffset - (byte*)&metadata);
        Assert.Equal(36, (byte*)&metadata.VerticalOffset - (byte*)&metadata);
    }

    [GhosttyNativeFact]
    public void CopiesExactNamespacesAndUpstreamResolvedVirtualRoot()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(10, 4);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\u001b_Ga=T,f=32,i=1,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        terminal.Write("\u001b_Ga=p,i=1,U=1,p=7,c=1,r=1\u001b\\"u8);
        terminal.Write("\u001b_Ga=T,f=32,i=2,p=9,s=1,v=1,P=1,Q=7,H=-3,V=2,C=1;AAD//w==\u001b\\"u8);
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        using GhosttyKittyGraphicsPlacementIterator iterator = new();
        graphics!.Populate(iterator);
        int seen = 0;
        while (iterator.MoveNext())
        {
            var metadata = iterator.GetMetadata(graphics);
            Assert.Equal(iterator.GetImageId(), metadata.ImageId);
            Assert.Equal(iterator.GetPlacementId(), metadata.PlacementId);
            if (metadata.ImageId == 2)
            {
                Assert.Equal(GhosttyVtNative.RoyalKittyPlacementFlags.VirtualRoot, metadata.Flags);
                Assert.Equal((1u, 7u, 0u, -3, 2), (metadata.RootImageId, metadata.RootPlacementId, metadata.RootInternal, metadata.HorizontalOffset, metadata.VerticalOffset));
            }
            else if (metadata.PlacementId == 7)
                Assert.Equal(GhosttyVtNative.RoyalKittyPlacementFlags.Virtual, metadata.Flags);
            else
                Assert.Equal(GhosttyVtNative.RoyalKittyPlacementFlags.Virtual | GhosttyVtNative.RoyalKittyPlacementFlags.InternalId, metadata.Flags);
            seen++;
        }
        Assert.Equal(3, seen);
        iterator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => iterator.GetMetadata(graphics));
    }

    [GhosttyNativeFact]
    public unsafe void RejectsInvalidArgumentsAndUnpositionedIterator()
    {
        GhosttyVtNative.RoyalKittyPlacementMetadata metadata = GhosttyVtNative.RoyalKittyPlacementMetadata.CreateSized();
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.KittyGraphicsPlacementMetadata(0, 0, &metadata));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.KittyGraphicsPlacementMetadata(0, 0, null));
        metadata.Size = 1;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.KittyGraphicsPlacementMetadata(0, 0, &metadata));
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(10, 4);
        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        using GhosttyKittyGraphicsPlacementIterator iterator = new();
        Assert.Throws<InvalidOperationException>(() => iterator.GetMetadata(graphics!));
        Assert.Throws<ArgumentNullException>(() => iterator.GetMetadata(null!));
    }
}
