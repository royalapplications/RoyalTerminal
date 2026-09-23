// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyGraphicsVtProcessorTests
{
    [Fact]
    public void DirectTransmissionAndPlacement_PublishesDecodedPixels()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process("\u001b_Ga=T,t=d,f=32,i=1,p=7,s=1,v=1,C=1;AQIDBA==\u001b\\"u8);

        TerminalKittyImagePlacement placement = Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.Equal(1, placement.ImageId);
        Assert.Equal(0, placement.ViewportColumn);
        Assert.Equal(0, placement.ViewportRow);
        Assert.Equal(0, processor.CursorCol);
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? source));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, source!.RgbaPixels.ToArray());
    }

    [Fact]
    public void ChunkedTransmission_CanBeDisplayedByLaterCommand()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);

        processor.Process("\u001b_Ga=t,t=d,f=32,i=2,s=1,v=1,m=1;AQID\u001b\\"u8);
        Assert.False(screen.HasKittyGraphics);
        processor.Process("\u001b_Gm=0;BA==\u001b\\"u8);
        processor.Process("\u001b_Ga=p,i=2,p=3,C=1\u001b\\"u8);

        Assert.Single(screen.GetKittyPlacements().ToArray());
        Assert.True(screen.TryGetKittyImageSource(2, out TerminalKittyImageSource? source));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, source!.RgbaPixels.ToArray());
    }

    [Fact]
    public void ResizeAndAlternateScreen_ReprojectAndRestorePlacements()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,t=d,f=32,i=5,p=1,s=1,v=1,C=1;AQIDBA==\u001b\\"u8);

        processor.ResizeScreen(8, 4, 80, 40, reflowOnResize: true, Span<TerminalGridPosition>.Empty);
        Assert.Single(screen.GetKittyPlacements().ToArray());

        processor.Process("\u001b[?1049h"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
        processor.Process("\u001b[?1049l"u8);
        Assert.Single(screen.GetKittyPlacements().ToArray());
    }
}
