// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttySnapshotImageProgressTests
{
    [GhosttyNativeFact]
    public void ReadyReportsPendingImagesAndNextRestoresPixels()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(10, 3);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\x1b_Ga=T,f=32,i=7,s=1,v=1,C=1;/wAA/w==\x1b\\"u8);
        using GhosttySnapshotDecoder decoder = new(GhosttySnapshot.Encode(terminal));
        decoder.SetKittyImageStorageLimit(512);
        Assert.Equal(512ul, decoder.GetKittyImageStorageLimit());
        using GhosttyTerminal restored = decoder.Ready();
        Assert.Equal(1u, decoder.GetPrimaryKittyImages());
        Assert.Equal(4ul, decoder.GetPrimaryKittyPendingBytes());
        Assert.Null(decoder.GetAlternateKittyImages());
        Assert.Null(decoder.GetAlternateKittyPendingBytes());
        Assert.Null(decoder.GetProgressKind());
        Assert.Null(decoder.GetProgressImageId());
        Assert.True(restored.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(7, out var pending));
        Assert.Empty(pending.CopyData());
        int imageSteps = 0;
        while (decoder.Next())
        {
            if (decoder.GetProgressKind() != GhosttySnapshotDecoderProgressKind.Image) continue;
            imageSteps++;
            Assert.Equal(GhosttyTerminalScreen.Primary, decoder.GetProgressScreen());
            Assert.Equal((uint?)7, decoder.GetProgressImageId());
            Assert.Equal((uint?)1, decoder.GetProgressImageFrame());
            Assert.Equal((nuint?)4, decoder.GetProgressImageBytes());
            Assert.Throws<InvalidOperationException>(() => decoder.GetProgressRows());
        }
        Assert.Equal(1, imageSteps);
        Assert.Null(decoder.GetProgressKind());
        Assert.True(graphics.TryGetImage(7, out var complete));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, complete.CopyData());
    }

    [GhosttyNativeFact]
    public void ZeroImageBudgetConsumesSnapshotWithoutRestoringImages()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        using GhosttyTerminal terminal = new(10, 3);
        terminal.SetKittyImageStorageLimit(1024);
        terminal.Write("\x1b_Ga=T,f=32,i=7,s=1,v=1,C=1;/wAA/w==\x1b\\"u8);
        using GhosttySnapshotDecoder decoder = new(GhosttySnapshot.Encode(terminal));
        decoder.SetKittyImageStorageLimit(0);
        using GhosttyTerminal restored = decoder.Ready();
        Assert.Equal(0u, decoder.GetPrimaryKittyImages());
        while (decoder.Next())
        {
            if (decoder.GetProgressKind() == GhosttySnapshotDecoderProgressKind.Image)
                Assert.Equal((nuint?)0, decoder.GetProgressImageBytes());
        }
        if (restored.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics))
            Assert.False(graphics!.TryGetImage(7, out _));
    }
}
