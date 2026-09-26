// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Follows pinned Ghostty graphics_exec/graphics_storage for Kitty APC semantics.
/// Windows Terminal's closest graphics dispatcher is SIXEL. xterm.js addon-image
/// also retains first-chunk metadata, but its single-placement store and deletion
/// TODOs are not the target for Ghostty's relative/virtual/animation behavior.
/// Configurable managed byte bounds remain enforced when a chunk is rejected.
/// </summary>
public sealed class ManagedKittyTransferLifecycleTests
{
    [Theory]
    [InlineData("a=t,i=1,f=99,s=1,v=1;AQIDBA==", "EINVAL: unsupported format")]
    [InlineData("a=T,i=1,t=s,f=32,s=1,v=1;L2ltYWdl", "EINVAL: unsupported medium")]
    [InlineData("a=t,i=1,f=32,s=0,v=1;AQIDBA==", "EINVAL: dimensions required")]
    [InlineData("a=t,i=1,f=32,s=1,v=1;AQI=", "EINVAL: invalid data")]
    public void FailedRetransmissionRetiresOldImageAndRelativeDescendants(string replacement, string error)
    {
        using Session session = new();
        session.Send("a=T,i=1,p=1,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=T,i=2,p=1,P=1,Q=1,H=1,s=1,v=1,C=1;BQYHCA==");
        Assert.Equal(2, session.Screen.GetKittyPlacements().Length);
        Assert.True(session.Screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? retained));
        session.Replies.Clear();

        session.Send(replacement);

        Assert.Equal($"\x1b_Gi=1;{error}\x1b\\", Assert.Single(session.Replies));
        Assert.Empty(session.Screen.GetKittyPlacements().ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, retained!.RgbaPixels);
        session.Send("a=p,i=1,C=1");
        Assert.Equal("\x1b_Gi=1;ENOENT: image not found\x1b\\", session.Replies[^1]);
        // Relative placements are removed, but the child's independent data survives.
        session.Send("a=p,i=2,C=1");
        Assert.Equal(2, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
    }

    [Theory]
    [InlineData("a=t,i=1,I=2,f=99,s=1,v=1;AQIDBA==")]
    [InlineData("a=t,i=1,f=-1,s=1,v=1;AQIDBA==")]
    [InlineData("a=q,i=1,f=99,s=1,v=1;AQIDBA==")]
    public void ConflictMalformedCommandAndQueryDoNotRetireExistingImage(string command)
    {
        using Session session = new();
        session.Send("a=T,i=1,p=1,s=1,v=1,C=1;AQIDBA==");
        session.Send(command);
        Assert.Equal(1, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
        Assert.True(session.Screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? image));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image!.RgbaPixels);
    }

    [Theory]
    [InlineData("i=99", "i=99")]
    [InlineData("I=99", "I=99")]
    public void VirtualParentIsRejectedBeforeLookingUpMissingImage(string selector, string response)
    {
        using Session session = new();
        session.Send($"a=p,{selector},p=3,U=1,P=1");
        Assert.Equal($"\x1b_G{response},p=3;EINVAL: virtual placement cannot refer to a parent\x1b\\",
            Assert.Single(session.Replies));
    }

    [Theory]
    [InlineData("a=t,f=32,s=1,v=1,m=1;AQI=", true, 2147483648u)]
    [InlineData("a=t,f=32,s=1,v=1;AQI=", false, 2147483648u)]
    [InlineData("a=t,f=99,s=1,v=1;AQI=", false, 2147483647u)]
    public void ImplicitIdIsAssignedWhenLoadingStartsNotWhenItCompletes(string first, bool abort, uint nextId)
    {
        using Session session = new();
        session.Send(first);
        if (abort) session.Send("a=d,d=a");
        session.Send("a=T,s=1,v=1,C=1;AQIDBA==");
        Assert.Empty(session.Replies);
        Assert.Equal(unchecked((int)nextId), Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedContinuationCanBeRetriedWithoutLosingInitialMetadata(bool animation)
    {
        using Session session = new(imageLimit: 4);
        if (animation) session.Send("a=T,i=7,p=1,s=1,v=1,C=1;/wAA/w==");
        session.Replies.Clear();
        session.Send($"a={(animation ? 'f' : 'T')},i=7,p=9,s=1,v=1,C=1,m=1;AQI=");
        session.Send("a=T,i=99,p=12,s=9,v=9,m=0;AQIDBA==");
        Assert.Equal("\x1b_Gi=7,p=9;EINVAL: invalid data\x1b\\", Assert.Single(session.Replies));
        session.Send("a=t,i=123,s=8,v=8,m=0;AwQ=");
        Assert.Equal($"\x1b_Gi=7,p=9{(animation ? ",r=2" : "")};OK\x1b\\", session.Replies[^1]);
        if (animation) session.Send("a=a,i=7,c=2");
        Assert.True(session.Screen.TryGetKittyImageSource(7, out TerminalKittyImageSource? image));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image!.RgbaPixels);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void RejectedChunkRetainsNonzeroQuietOverrideForRetry(int quiet, int replyCount)
    {
        using Session session = new(imageLimit: 4);
        session.Send("a=T,i=7,s=1,v=1,C=1,m=1;AQI=");
        session.Send($"m=0,q={quiet};AQIDBA==");
        session.Send("m=0,q=0;AwQ=");
        Assert.Equal(replyCount, session.Replies.Count);
        Assert.Equal(7, Assert.Single(session.Screen.GetKittyPlacements().ToArray()).ImageId);
    }

    [Fact]
    public void FailedNumberedImageAdmissionDoesNotReplyWithUnstoredAssignedId()
    {
        using Session session = new(storageLimit: 3);
        session.Send("a=t,I=77,s=1,v=1;AQIDBA==");
        Assert.Equal("\x1b_GI=77;ENOMEM: out of memory\x1b\\", Assert.Single(session.Replies));
        session.Send("a=p,i=1,C=1");
        Assert.Equal("\x1b_Gi=1;ENOENT: image not found\x1b\\", session.Replies[^1]);
    }

    [Theory]
    [InlineData("a=f,i=7,r=99,f=99,s=1,v=1;AQIDBA==", ",r=99", "EINVAL: unsupported format")]
    [InlineData("a=f,i=7,r=99,s=1,v=1;AQI=", "", "ENODATA: insufficient data")]
    [InlineData("a=f,i=7,r=99,s=2,v=1;AQIDBAUGBwg=", "", "EINVAL: frame dimensions exceed image")]
    [InlineData("a=f,i=7,r=99,s=1,v=1,c=99;AQIDBA==", ",r=2", "EINVAL: base frame not found")]
    public void AnimationErrorsOnlyIncludeFrameNumberAtUpstreamResolutionStage(string command, string frame, string error)
    {
        using Session session = new();
        session.Send("a=t,i=7,s=1,v=1;/wAA/w==");
        session.Replies.Clear();
        session.Send(command);
        Assert.Equal($"\x1b_Gi=7{frame};{error}\x1b\\", Assert.Single(session.Replies));
    }

    [Theory]
    [InlineData(false, 0u, false, 1u, 2)]
    [InlineData(true, 0u, true, 1u, 2)]
    [InlineData(false, 0u, true, 1u, 1)]
    [InlineData(true, 1u, false, 0u, 2)]
    [InlineData(false, 1u, false, 0u, 1)]
    [InlineData(false, 0u, false, 2u, 1)]
    [InlineData(false, 0u, false, 3u, 2)]
    [InlineData(false, 0u, false, 4294967295u, 2)]
    public void EvictionUsesUsageLowBitThenPlacementPriorityThenGeneration(
        bool firstPlaced, uint firstHint, bool secondPlaced, uint secondHint, int evicted)
    {
        using Session session = new(storageLimit: 8);
        session.Send($"a={(firstPlaced ? 'T' : 't')},i=1,N={firstHint},s=1,v=1,C=1;AQIDBA==");
        // Only the initial chunk supplies the usage hint.
        session.Send($"a={(secondPlaced ? 'T' : 't')},i=2,N={secondHint},s=1,v=1,C=1,m=1;BQY=");
        session.Send($"m=0,N={secondHint ^ 1};Bwg=");
        session.Send("a=t,i=3,s=1,v=1;CQoLDA==");
        session.Replies.Clear();
        session.Send("a=p,i=1,p=2,C=1");
        session.Send("a=p,i=2,p=2,C=1");
        session.Send("a=p,i=3,p=2,C=1");
        Assert.Equal(3, session.Replies.Count);
        for (int id = 1; id <= 3; id++)
            Assert.Equal($"\x1b_Gi={id},p=2;{(id == evicted ? "ENOENT: image not found" : "OK")}\x1b\\",
                session.Replies[id - 1]);
    }

    private sealed class Session : IDisposable
    {
        internal TerminalScreen Screen { get; } = new(8, 5, 100);
        internal BasicVtProcessor Processor { get; }
        internal List<string> Replies { get; } = [];

        internal Session(int imageLimit = 1024, int storageLimit = 4096)
        {
            Processor = new(Screen, new()
            {
                KittyGraphicsMaxImageBytes = imageLimit,
                KittyGraphicsStorageLimitBytes = storageLimit,
            });
            Processor.NotifyResize(8, 5, 80, 50);
            Processor.ResponseCallback = bytes => Replies.Add(Encoding.ASCII.GetString(bytes));
        }

        internal void Send(string command) => Processor.Process(Encoding.ASCII.GetBytes($"\x1b_G{command}\x1b\\"));
        public void Dispose() => Processor.Dispose();
    }
}
