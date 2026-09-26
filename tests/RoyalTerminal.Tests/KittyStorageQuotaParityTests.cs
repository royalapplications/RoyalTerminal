// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Ghostty graphics_storage/graphics_exec are the quota and animation references.
/// xterm.js addon-image retains source formats in blobs but uses a different,
/// count-based undisplayed-image policy; Windows Terminal's graphics parser is
/// SIXEL. Neither is substituted for Ghostty's byte-admission rules here.
/// </summary>
public sealed class KittyStorageQuotaParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RgbAdmissionAndRenderCopiesUseDifferentByteCounts(bool native)
    {
        using Session session = new(native, 6);
        session.Send("a=T,i=1,f=24,s=1,v=1,C=1;AQID");
        session.Send("a=T,i=2,f=24,s=1,v=1,C=1;BQYH");
        byte[] retained = session.Pixels(1);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, retained);
        Assert.Equal(new byte[] { 5, 6, 7, 255 }, session.Pixels(2));
        session.Send("a=T,i=3,f=24,s=1,v=1,C=1;CQoL");
        Assert.False(session.Exists(1));
        Assert.True(session.Exists(2));
        Assert.True(session.Exists(3));
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, retained);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RgbPromotionAndExistingFrameEditsAreExemptFromAdmissionQuota(bool native)
    {
        using Session session = new(native, 3);
        session.Send("a=T,i=1,f=24,s=1,v=1,C=1;/wAA");
        byte[] before = session.Pixels(1);
        session.Send("a=f,i=1,r=1,f=24,s=1,v=1;AAD/");
        Assert.Equal("\x1b_Gi=1,r=1;OK\x1b\\", session.LastReply);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Pixels(1));
        session.Send("a=f,i=1,r=1,s=1,v=1;AP8A/w==");
        Assert.Equal("\x1b_Gi=1,r=1;OK\x1b\\", session.LastReply);
        session.Send("a=f,i=1,f=24,s=1,v=1;AAD/");
        Assert.Equal("\x1b_Gi=1,r=2;ENOSPC: animation frame storage full\x1b\\", session.LastReply);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, session.Pixels(1));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, before);
        session.Send("a=T,i=2,f=24,s=1,v=1,C=1;AQID");
        Assert.False(session.Exists(1));
        Assert.True(session.Exists(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseFrameValidationFollowsPromotionWithoutEviction(bool native)
    {
        using Session session = new(native, 7);
        session.Send("a=T,i=1,f=24,s=1,v=1,C=1;/wAA");
        session.Send("a=T,i=2,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=f,i=1,c=99,f=24,s=1,v=1;AAD/");
        Assert.Equal("\x1b_Gi=1,r=2;EINVAL: base frame not found\x1b\\", session.LastReply);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, session.Pixels(1));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, session.Pixels(2));
        // Promotion stamps image 1 even on this failure. With both placed,
        // image 2 is now older and is the next admission's eviction victim.
        session.Send("a=T,i=3,f=24,s=1,v=1,C=1;BQYH");
        Assert.True(session.Exists(1));
        Assert.False(session.Exists(2));
        Assert.True(session.Exists(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAppendCanEvictOtherImagesButNeverItsOwnTarget(bool native)
    {
        using Session session = new(native, 7);
        session.Send("a=T,i=1,s=1,v=1,C=1;/wAA/w==");
        session.Send("a=t,i=2,f=24,s=1,v=1;AQID");
        session.Send("a=f,i=1,s=1,v=1;AAD//w==");
        Assert.Equal("\x1b_Gi=1,r=2;ENOSPC: animation frame storage full\x1b\\", session.LastReply);
        Assert.True(session.Exists(1));
        Assert.False(session.Exists(2));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, session.Pixels(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcessiveDeficitIsRejectedBeforeEvictionButAfterPromotion(bool native)
    {
        using Session session = new(native, 9);
        session.Send("a=T,i=1,f=24,s=2,v=1,C=1;/wAAAP8A");
        session.Send("a=t,i=2,f=24,s=1,v=1;AQID");
        // After root promotion, 11 retained + 8 requested - 9 limit = 10,
        // which exceeds the limit and triggers Ghostty's early guard.
        session.Send("a=f,i=1,f=24,s=1,v=1;AAD/");
        Assert.Equal("\x1b_Gi=1,r=2;ENOSPC: animation frame storage full\x1b\\", session.LastReply);
        Assert.True(session.Exists(1));
        Assert.True(session.Exists(2));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, session.Pixels(1));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InvalidDimensionsOrCompositionDoNotPromoteOrRefreshEvictionAge(bool native, bool compose)
    {
        using Session session = new(native, 6);
        session.Send("a=T,i=1,f=24,s=1,v=1,C=1;/wAA");
        session.Send("a=T,i=2,f=24,s=1,v=1,C=1;AP8A");
        session.Send(compose ? "a=c,i=1,r=1,c=1,w=1,h=1" : "a=f,i=1,f=24,s=2,v=1;AAD/AAD/");
        Assert.Equal($"\x1b_Gi=1;EINVAL: {(compose ? "source and destination rectangles overlap" : "frame dimensions exceed image")}\x1b\\",
            session.LastReply);
        session.Send("a=T,i=3,f=24,s=1,v=1,C=1;AQID");
        Assert.False(session.Exists(1));
        Assert.True(session.Exists(2));
        Assert.True(session.Exists(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidSelfCompositionPromotesRgbWithoutQuotaAdmission(bool native)
    {
        using Session session = new(native, 6);
        session.Send("a=T,i=1,f=24,s=2,v=1,C=1;/wAAAP8A");
        byte[] before = session.Pixels(1);
        session.Send("a=c,i=1,r=1,c=1,X=0,x=1,w=1,h=1,C=1");
        Assert.Equal("\x1b_Gi=1;OK\x1b\\", session.LastReply);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255 }, session.Pixels(1));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, before);
        session.Send("a=f,i=1,r=1,f=24,s=1,v=1;AAD/");
        Assert.Equal("\x1b_Gi=1,r=1;OK\x1b\\", session.LastReply);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootFrameDeletionCreditsBytesWithoutDiscardingPromotedFrame(bool native)
    {
        using Session session = new(native, 8);
        session.Send("a=T,i=1,f=24,s=1,v=1,C=1;/wAA");
        session.Send("a=f,i=1,f=24,s=1,v=1;AAD/");
        Assert.Equal("\x1b_Gi=1,r=2;OK\x1b\\", session.LastReply);
        session.Send("a=d,d=f,i=1,r=1");
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, session.Pixels(1));
        session.Send("a=T,i=2,f=24,s=1,v=1,C=1;AQID");
        Assert.True(session.Exists(1));
        Assert.True(session.Exists(2));
        session.Send("a=d,d=F,i=1");
        Assert.False(session.Exists(1));
        session.Send("a=T,i=3,s=2,v=1,C=1;AQIDBAUGBwg=");
        Assert.False(session.Exists(2));
        Assert.True(session.Exists(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetransmissionReleasesAllAnimationFramesAndReturnsToRgbAccounting(bool native)
    {
        using Session session = new(native, 8);
        session.Send("a=T,i=1,s=1,v=1,C=1;/wAA/w==");
        session.Send("a=f,i=1,s=1,v=1;AAD//w==");
        session.Send("a=T,i=1,f=24,s=1,v=1,C=1;AP8A");
        session.Send("a=T,i=2,s=1,v=1,C=1;AQIDBA==");
        session.Send("a=a,i=1,c=2");
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, session.Pixels(1));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, session.Pixels(2));
    }

    private sealed class Session : IDisposable
    {
        private readonly TerminalScreen _screen = new(8, 3, 10);
        private readonly BasicVtProcessor? _managed;
        private readonly GhosttyTerminal? _native;
        private readonly GhosttyVtNative.GhosttyTerminalWritePtyCallback? _write;
        private readonly List<string> _replies = [];
        internal string LastReply => _replies[^1];

        internal Session(bool native, int limit)
        {
            if (native)
            {
                if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
                {
                    Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
                    Assert.Skip("Native Ghostty Kitty graphics runtime unavailable.");
                }
                _native = new(8, 3);
                _native.Resize(8, 3, 10, 10);
                _native.SetKittyImageStorageLimit((ulong)limit);
                _write = (_, _, data, length) => _replies.Add(Marshal.PtrToStringUTF8(data, checked((int)length))!);
                _native.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(_write));
            }
            else
            {
                _managed = new(_screen, new() { KittyGraphicsStorageLimitBytes = limit });
                _managed.NotifyResize(8, 3, 80, 30);
                _managed.ResponseCallback = bytes => _replies.Add(Encoding.ASCII.GetString(bytes));
            }
        }

        internal void Send(string command)
        {
            byte[] bytes = Encoding.ASCII.GetBytes($"\x1b_G{command}\x1b\\");
            if (_native is not null) _native.Write(bytes);
            else _managed!.Process(bytes);
        }

        internal bool Exists(uint id)
        {
            int count = _replies.Count;
            Send($"a=p,i={id},p=1,C=1");
            Assert.Equal(count + 1, _replies.Count);
            string ok = $"\x1b_Gi={id},p=1;OK\x1b\\";
            if (LastReply == ok) return true;
            Assert.Equal($"\x1b_Gi={id},p=1;ENOENT: image not found\x1b\\", LastReply);
            return false;
        }

        internal byte[] Pixels(uint id)
        {
            if (_native is not null)
            {
                Assert.True(_native.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
                Assert.True(graphics!.TryGetImage(id, out GhosttyKittyGraphicsImage image));
                return image.CopyRgbaData();
            }
            Assert.True(_screen.TryGetKittyImageSource(unchecked((int)id), out TerminalKittyImageSource? source));
            return source!.RgbaPixels;
        }

        public void Dispose()
        {
            _managed?.Dispose();
            if (_native is not null)
            {
                _native.SetWritePtyCallback(0);
                _native.Dispose();
            }
            GC.KeepAlive(_write);
        }
    }
}
