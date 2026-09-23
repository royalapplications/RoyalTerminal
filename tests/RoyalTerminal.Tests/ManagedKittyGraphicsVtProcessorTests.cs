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

    [Fact]
    public void Animation_AdvancesWithoutFurtherTerminalInput()
    {
        TestClock clock = new();
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=f,f=32,i=1,s=1,v=1,z=40;AAD//w==\u001b\\"u8);
        processor.Process("\u001b_Ga=a,i=1,r=1,z=40,s=3\u001b\\"u8);
        Assert.Equal(TimeSpan.FromMilliseconds(40), processor.NextTimedRefreshDelay);

        clock.Advance(TimeSpan.FromMilliseconds(39));
        Assert.False(processor.RefreshTimedState());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(processor.RefreshTimedState());
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? frame));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, frame!.RgbaPixels);

        clock.Advance(TimeSpan.FromMilliseconds(40));
        Assert.True(processor.RefreshTimedState());
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? rootFrame));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rootFrame!.RgbaPixels);

        processor.Process("\u001b_Ga=a,i=1,s=1\u001b\\"u8);
        Assert.Null(processor.NextTimedRefreshDelay);
    }

    [Fact]
    public void LowercaseDeletionRetainsImageData_UppercaseDeletionReleasesIt()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,t=d,f=32,i=3,p=1,s=1,v=1,C=1;AQIDBA==\u001b\\"u8);

        processor.Process("\u001b_Ga=d,d=i,i=3\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
        processor.Process("\u001b_Ga=p,i=3,p=2,C=1\u001b\\"u8);
        Assert.Single(screen.GetKittyPlacements().ToArray());

        processor.Process("\u001b_Ga=d,d=I,i=3\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
        processor.Process("\u001b_Ga=p,i=3,p=3,C=1\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
    }

    [Fact]
    public void AllPlacementsDeletion_PreservesUnplacedImageUntilUppercaseRangeDelete()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,t=d,f=32,i=4,p=1,s=1,v=1,C=1;AQIDBA==\u001b\\"u8);

        processor.Process("\u001b_Ga=d,d=a\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
        processor.Process("\u001b_Ga=p,i=4,p=2,C=1\u001b\\"u8);
        Assert.Single(screen.GetKittyPlacements().ToArray());

        processor.Process("\u001b_Ga=d,d=R,x=4,y=4\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
        processor.Process("\u001b_Ga=p,i=4,p=3,C=1\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
    }

    [Fact]
    public void CellAndColumnSelectors_DeleteOnlyIntersectingPlacements()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,t=d,f=32,i=6,p=1,s=1,v=1,C=1;AQIDBA==\u001b\\"u8);
        processor.Process("\u001b_Ga=d,d=p,x=2,y=1\u001b\\"u8);
        Assert.Single(screen.GetKittyPlacements().ToArray());

        processor.Process("\u001b_Ga=d,d=p,x=1,y=1\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
        processor.Process("\u001b_Ga=p,i=6,p=2,C=1\u001b\\"u8);
        Assert.Single(screen.GetKittyPlacements().ToArray());
        processor.Process("\u001b_Ga=d,d=x,x=1\u001b\\"u8);
        Assert.True(screen.GetKittyPlacements().IsEmpty);
    }

    [Fact]
    public void InvalidAnimationBase_DoesNotEvictAnotherImageBeforeValidation()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen, new() { KittyGraphicsStorageLimitBytes = 8 });
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=t,f=32,i=2,s=1,v=1;AAD//w==\u001b\\"u8);
        processor.Process("\u001b_Ga=f,f=32,i=1,s=1,v=1,c=99;AP8A/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=p,i=2,p=1,C=1\u001b\\"u8);
        Assert.Equal(2, screen.GetKittyPlacements().Length);
        Assert.True(screen.TryGetKittyImageSource(2, out _));
    }

    [Fact]
    public void FailedReplacement_RemovesRelativePlacementFromPublishedSnapshot()
    {
        TerminalScreen screen = new(10, 4, 10);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(10, 4, 100, 40);
        processor.Process("\u001b_Ga=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==\u001b\\"u8);
        processor.Process("\u001b_Ga=T,f=32,i=2,p=1,s=1,v=1,P=1,Q=1,H=1,C=1;AAD//w==\u001b\\"u8);
        Assert.Equal(2, screen.GetKittyPlacements().Length);
        processor.Process("\u001b_Ga=t,f=32,i=2,s=1,v=1;\u001b\\"u8);
        Assert.Equal(1, Assert.Single(screen.GetKittyPlacements().ToArray()).ImageId);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
