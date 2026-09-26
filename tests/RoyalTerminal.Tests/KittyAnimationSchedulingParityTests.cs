// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty advances animations at renderer/host publication boundaries, not
/// between commands parsed by one terminal write. RoyalTerminal's two adapters
/// share that ordering, including the explicit DECSET-2026 prefix publication.
/// xterm.js addon-image leaves animation actions unimplemented; Windows Terminal
/// dispatches SIXEL rather than Kitty graphics, so neither supplies this clock.
/// </summary>
public sealed class KittyAnimationSchedulingParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopAtFrameDeadlineAppliesBeforeTheRenderTick(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        TerminalKittyImageSource retained = session.Image();
        session.Clock.Advance(40);

        session.Send("a=a,i=1,s=1");

        Assert.Same(retained, session.Image());
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, session.Image().RgbaPixels);
        Assert.Null(session.Timer.NextTimedRefreshDelay);
        session.Clock.Advance(1000);
        Assert.False(session.Timer.RefreshTimedState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtendingCurrentGapAtDeadlineRetainsElapsedTime(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        session.Clock.Advance(40);
        session.Send("a=a,i=1,r=1,z=100");
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(60), session.Timer.NextTimedRefreshDelay);
        session.Clock.Advance(59);
        Assert.False(session.Timer.RefreshTimedState());
        session.Clock.Advance(1);
        Assert.True(session.Timer.RefreshTimedState());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingDisplayedFrameAtDeadlineRestartsItsOwnTimer(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        TerminalKittyImageSource retained = session.Image();
        session.Clock.Advance(40);
        session.Send("a=f,i=1,r=1,s=1,v=1;AP8A/w==");
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, session.Image().RgbaPixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, retained.RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
        session.Clock.Advance(40);
        Assert.True(session.Timer.RefreshTimedState());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompositionChangesPixelsWithoutRestartingCurrentGap(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        TerminalKittyImageSource retained = session.Image();
        session.Clock.Advance(20);
        session.Send("a=c,i=1,r=2,c=1,C=1");
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, retained.RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(20), session.Timer.NextTimedRefreshDelay);
        session.Clock.Advance(20);
        Assert.True(session.Timer.RefreshTimedState());
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyPublicationBoundariesCanInvalidateAnInFlightFrame(bool native, bool singleWrite)
    {
        using Session session = new(native);
        session.StartTwoFrames(run: false);
        session.Replies.Clear();
        string first = Session.Wire("a=f,i=1,s=1,v=1,m=1;AP8A");
        string run = Session.Wire("a=a,i=1,s=3"); // Gapless root advances when published.
        string last = Session.Wire("m=0;/w==");
        if (singleWrite) session.Write(first + run + last);
        else
        {
            session.Write(first);
            session.Write(run);
            session.Write(last);
        }
        Assert.Equal(singleWrite ? "\u001b_Gi=1,r=3;OK\u001b\\" : "\u001b_Gi=1;ENOENT: image not found\u001b\\",
            Assert.Single(session.Replies));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopAfterSynchronizedReleaseAppliesBeforeNextAnimationTick(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        session.Write("\u001b[?2026h");
        session.Clock.Advance(40);
        session.Write("\u001b[?2026l" + Session.Wire("a=a,i=1,s=1"));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, session.Image().RgbaPixels);
        Assert.Null(session.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronizedPrefixTicksBeforeRemainingCommandsInTheSameWrite(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames(run: false);
        session.Replies.Clear();
        session.Write(Session.Wire("a=f,i=1,s=1,v=1,m=1;AP8A") +
            Session.Wire("a=a,i=1,s=3") + "\u001b[?2026h" + Session.Wire("m=0;/w=="));
        Assert.Equal("\u001b_Gi=1;ENOENT: image not found\u001b\\", Assert.Single(session.Replies));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromSeconds(1), session.Timer.NextTimedRefreshDelay);
        session.Write("\u001b[?2026l");
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntermediateScreenSwitchDoesNotTickTheDepartedImage(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        session.Clock.Advance(40);
        session.Write("\u001b[?47h\u001b[?47l" + Session.Wire("a=a,i=1,s=1"));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, session.Image().RgbaPixels);
        Assert.Null(session.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModeObserversSeeTheCompletedBatchAnimation(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames(run: false);
        List<byte[]> observed = [];
        session.Processor.ModeChanged += (_, _) => observed.Add(session.Image().RgbaPixels.ToArray());
        session.Write(Session.Wire("a=a,i=1,s=3") + "\u001b[?1004h");
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Assert.Single(observed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalResizeRefreshesTheActiveAnimation(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames();
        session.Clock.Advance(40);
        session.Processor.NotifyResize(8, 3, 128, 96);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
    }

    [Fact]
    public void ProcessUntilGroundPublishesOnlyItsConsumedPrefix()
    {
        using Session session = new(false);
        session.StartTwoFrames(run: false);
        session.Write("\u001b_Ga=a,i=1,s=3");
        byte[] suffix = Encoding.ASCII.GetBytes("\u001b\\" + Session.Wire("a=a,i=1,s=1"));
        BasicVtProcessor managed = (BasicVtProcessor)session.Processor;
        Assert.True(managed.ProcessUntilGround(suffix, out int consumed));
        Assert.Equal(2, consumed);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
        managed.Process(suffix.AsSpan(consumed));
        Assert.Null(session.Timer.NextTimedRefreshDelay);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void DeletingEarlierFrameWhileShowingLastPreservesPixelsAndElapsedGap(bool native, int removed)
    {
        using Session session = new(native);
        session.StartTwoFrames(run: false);
        session.Send("a=f,i=1,s=1,v=1,z=60;AP8A/w==");
        session.Send("a=a,i=1,c=3,s=3");
        TerminalKittyImageSource retained = session.Image();
        session.Clock.Advance(20);
        session.Send($"a=d,d=f,i=1,r={removed}");
        Assert.Same(retained, session.Image());
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
        session.Clock.Advance(40);
        Assert.True(session.Timer.RefreshTimedState());
        Assert.Equal(removed == 1 ? new byte[] { 0, 0, 255, 255 } : new byte[] { 0, 255, 0, 255 },
            session.Image().RgbaPixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingPlaybackWakesOnAppendAndPlacementControlsScheduling(bool native)
    {
        using Session session = new(native);
        session.StartTwoFrames(run: false);
        session.Send("a=a,i=1,s=2");
        session.Clock.Advance(40);
        Assert.False(session.Timer.RefreshTimedState());
        Assert.Null(session.Timer.NextTimedRefreshDelay);
        session.Send("a=f,i=1,s=1,v=1,z=25;AP8A/w==");
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(25), session.Timer.NextTimedRefreshDelay);
        session.Send("a=d,d=i,i=1");
        Assert.Null(session.Timer.NextTimedRefreshDelay);
        session.Clock.Advance(100);
        session.Send("a=p,i=1,p=1,C=1");
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, session.Image().RgbaPixels);
        Assert.Null(session.Timer.NextTimedRefreshDelay); // Loading parks at the last frame.
        session.Send("a=a,i=1,s=3");
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Image().RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), session.Timer.NextTimedRefreshDelay);
    }

    private sealed class Session : IDisposable
    {
        internal Clock Clock { get; } = new();
        internal TerminalScreen Screen { get; } = new(8, 3, 10);
        internal IVtProcessor Processor { get; }
        internal ITerminalTimedRefreshSource Timer => (ITerminalTimedRefreshSource)Processor;
        internal List<string> Replies { get; } = [];

        internal Session(bool native)
        {
            if (native && (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics))
            {
                Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
                Assert.Skip("Native Ghostty Kitty graphics runtime unavailable.");
            }
            Processor = native ? new GhosttyVtProcessor(Screen, Clock) : new BasicVtProcessor(Screen, new() { TimeProvider = Clock });
            Processor.ResponseCallback = bytes => Replies.Add(Encoding.ASCII.GetString(bytes));
            Processor.NotifyResize(8, 3, 64, 48);
        }

        internal void StartTwoFrames(bool run = true)
        {
            Send("a=T,i=1,p=1,s=1,v=1,C=1;/wAA/w==");
            Send("a=f,i=1,s=1,v=1,z=40;AAD//w==");
            if (run) Send("a=a,i=1,r=1,z=40,s=3");
        }

        internal TerminalKittyImageSource Image()
        {
            Assert.True(Screen.TryGetKittyImageSource(1, out var image));
            return image!;
        }

        internal static string Wire(string command) => "\u001b_G" + command + "\u001b\\";
        internal void Send(string command) => Write(Wire(command));
        internal void Write(string text) => Processor.Process(Encoding.ASCII.GetBytes(text));
        public void Dispose() => Processor.Dispose();
    }

    private sealed class Clock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(long milliseconds) => _timestamp += milliseconds;
    }
}
