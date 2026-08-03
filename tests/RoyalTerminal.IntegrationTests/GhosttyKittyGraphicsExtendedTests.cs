// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyKittyGraphicsExtendedTests
{
    [GhosttyNativeFact]
    public void ImageAndStorageGenerationStampsAreMonotonic()
    {
        if (!GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            return;
        }

        using GhosttyTerminal terminal = new(80, 24);
        terminal.Resize(80, 24, 8, 16);
        terminal.SetKittyImageStorageLimit(32UL * 1024UL * 1024UL);
        terminal.SetKittyImageMediumFile(enabled: true);
        terminal.SetKittyImageMediumTempFileDirectory(Path.GetTempPath());
        terminal.Write("\u001b_Ga=T,t=d,f=24,i=1,p=1,s=1,v=2,c=10,r=1;////////\u001b\\"u8);

        Assert.True(terminal.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.NotNull(graphics);
        Assert.True(graphics!.TryGetImage(1, out GhosttyKittyGraphicsImage image));
        Assert.True(graphics.GetGeneration() > 0);
        Assert.InRange(image.GetGeneration(), 1ul, graphics.GetGeneration());
    }
}
