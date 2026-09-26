// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Glyphs;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedGlyphProtocolTests(ITestOutputHelper output)
{
    private const string EmptyGlyph = "AAAAAAAAAAAAAA==";

    [Theory]
    [InlineData("s")]
    [InlineData("s;ignored=value")]
    [InlineData("q;cp=e000")]
    [InlineData("q;cp=1fffff")]
    [InlineData("q;cp=200000")]
    [InlineData("q;cp=+e_0__00")]
    [InlineData("q;cp=0xe000")]
    [InlineData("c;cp=oops")]
    [InlineData("c;cp=")]
    [InlineData("c;cp=41")]
    [InlineData("c;cp=-0")]
    [InlineData("c")]
    [InlineData("r;cp=e000;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=41;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=41;bad")]
    [InlineData("r;cp=dfff;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=f8ff;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=f900;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=f0000;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=ffffd;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=ffffe;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=100000;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=10fffd;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=10fffe;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;fmt=colrv0;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;reply=0;bad")]
    [InlineData("r;cp=e000;reply=2;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;reply=2;bad")]
    [InlineData("r;cp=e000;reply=unknown;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;upm=2_000;aw=1500;lh=+2200;width=2;size=stretch;align=end,baseline;pad=.1,+.2,-0,0.3;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;upm=0;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;upm=4294967296;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;cp=e001;upm=2000;upm=1000;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;pad=0.5,0,0.5,0;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;pad=0,1e-1,0,0;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;pad=0,0,0,0,0;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;align=baseline,center;AAAAAAAAAAAAAA==")]
    [InlineData("r;cp=e000;AAAAAAAAAAAAAB==")]
    [InlineData("r;cp=e000;AAAAAAAA AAAA AA==")]
    [InlineData("r;cp=e000")]
    [InlineData("r")]
    [InlineData("r;;")]
    [InlineData("unknown")]
    public void RequestsAndResultingCoverageMatchNativeAtEverySplit(string command)
    {
        if (!NativeAvailable()) return;
        Compare(Wire(command) + Wire("q;cp=e000") + Wire("q;cp=e001"), everySplit: true);
    }

    [Theory]
    [InlineData("upm", "", "0", "-1", "1.0", "1e3", "_1", "1_", "+1", "4294967295")]
    [InlineData("width", "", "0", "3", "+1", "01", "1_", "2", "1", "bad")]
    [InlineData("size", "", "height", "advance", "contain", "cover", "stretch", "none", "HEIGHT", "bad")]
    [InlineData("align", "", "start,start", "center,center", "end,end", "start,baseline", "baseline,start", "center", "end,end,", "bad")]
    [InlineData("pad", "", "0,0,0,0", "1,0,0,0", "0,.25,0,.25", "0,+.25,0,-0", "0,nan,0,0", "0,0.0000000000000001,0,0", "0,-.5,0,0", "0,0,0,0,")]
    public void OptionValidationMatchesNative(string key, params string[] values)
    {
        if (!NativeAvailable()) return;
        foreach (string value in values) Compare(Wire($"r;cp=e000;{key}={value};{EmptyGlyph}") + Wire("q;cp=e000"));
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    [InlineData(0x1B)]
    [InlineData(0x9C)]
    [InlineData(0x9B)]
    public void AbortStillExecutesCompletedGlyphCommandLikeNative(int exit)
    {
        if (!NativeAvailable()) return;
        Compare("\u001b_25a1;r;cp=e000;" + EmptyGlyph + (char)exit + "\u001b\\" + Wire("q;cp=e000"), everySplit: true);
    }

    [Fact]
    public void MetadataDefaultsAndExplicitValuesAreOwnedBySessionAndBothScreens()
    {
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;{EmptyGlyph}")));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? original));
        Assert.Equal((1000u, 1000u, 1000u, (byte)1), (original.UnitsPerEm, original.AdvanceWidth, original.LineHeight, original.Width));
        Assert.Equal(new(TerminalGlyphSize.Height, TerminalGlyphAlignment.Center, TerminalGlyphAlignment.Center, 0, 0, 0, 0), original.Layout);
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;upm=2000;aw=1500;lh=2200;width=2;size=stretch;align=end,baseline;pad=.1,.2,0,.3;{EmptyGlyph}")));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? replacement));
        Assert.NotSame(original, replacement);
        Assert.Equal((2000u, 1500u, 2200u, (byte)2), (replacement.UnitsPerEm, replacement.AdvanceWidth, replacement.LineHeight, replacement.Width));
        Assert.Equal(new(TerminalGlyphSize.Stretch, TerminalGlyphAlignment.End, TerminalGlyphAlignment.Baseline, .1, .2, 0, .3), replacement.Layout);
        processor.Process("\u001b[?1049h"u8);
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? alternate));
        Assert.Same(replacement, alternate);
        processor.Process("\u001b[?1049l\u001b[!p"u8);
        Assert.Equal(1, screen.RegisteredGlyphCount);
        processor.Process("\u001bc"u8);
        Assert.Equal(0, screen.RegisteredGlyphCount);
        Assert.False(screen.TryGetRegisteredGlyph(0xE000, out _));
    }

    [Fact]
    public void FifoEvictionReplacementAndFailedRegistrationMatchNative()
    {
        if (!NativeAvailable()) return;
        StringBuilder commands = new();
        for (uint cp = 0xE000; cp < 0xE400; cp++) commands.Append(Wire($"r;cp={cp:x};reply=0;{EmptyGlyph}"));
        commands.Append(Wire($"r;cp=e000;reply=0;{EmptyGlyph}")); // Replacement becomes newest.
        commands.Append(Wire("r;cp=e001;bad")); // Failure neither removes nor reorders.
        commands.Append(Wire($"r;cp=e400;{EmptyGlyph}"));
        commands.Append(Wire("q;cp=e000") + Wire("q;cp=e001") + Wire("q;cp=e002") + Wire("q;cp=e400"));
        Compare(commands.ToString());
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor managed = new(screen);
        managed.Process(Encoding.ASCII.GetBytes(commands.ToString()));
        Assert.Equal(1024, screen.RegisteredGlyphCount);
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out _));
        Assert.False(screen.TryGetRegisteredGlyph(0xE001, out _));
        Assert.True(screen.TryGetRegisteredGlyph(0xE400, out _));
    }

    [Fact]
    public void SynchronizedOutputPublishesGlyphChangesOnlyAtRelease()
    {
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;{EmptyGlyph}") + "\u001b[?2026h" + Wire("c") + Wire($"r;cp=e001;{EmptyGlyph}")));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out _));
        Assert.False(screen.TryGetRegisteredGlyph(0xE001, out _));
        StringBuilder replies = new();
        processor.ResponseCallback = data => replies.Append(Encoding.ASCII.GetString(data));
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=e001")));
        Assert.Contains("status=glossary", replies.ToString());
        processor.Process("\u001b[?2026l"u8);
        Assert.False(screen.TryGetRegisteredGlyph(0xE000, out _));
        Assert.True(screen.TryGetRegisteredGlyph(0xE001, out _));
    }

    [Fact]
    public void DisabledGlyphCommandsAreIgnoredAndDoNotBecomeUnknownApcs()
    {
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen, new() { GlyphProtocolEnabled = false });
        List<byte[]> responses = [];
        List<TerminalUnknownSequence> unknown = [];
        processor.ResponseCallback = responses.Add;
        processor.UnknownSequenceCallback = unknown.Add;
        processor.Process(Encoding.ASCII.GetBytes(Wire("s") + Wire($"r;cp=e000;{EmptyGlyph}")));
        Assert.Empty(responses);
        Assert.Empty(unknown);
        Assert.Equal(0, screen.RegisteredGlyphCount);
        processor.GlyphProtocolEnabled = true;
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;{EmptyGlyph}")));
        Assert.Equal(1, screen.RegisteredGlyphCount);
        processor.GlyphProtocolEnabled = false;
        Assert.Equal(0, screen.RegisteredGlyphCount);
        // Protocol enablement is latched at identifier recognition, not at ST.
        processor.GlyphProtocolEnabled = true;
        processor.Process("\u001b_25a1;r;cp=e001;"u8);
        processor.GlyphProtocolEnabled = false;
        processor.Process(Encoding.ASCII.GetBytes(EmptyGlyph + "\u001b\\"));
        Assert.True(screen.TryGetRegisteredGlyph(0xE001, out _));
    }

    [Theory]
    [InlineData(1024 * 1024, true)]
    [InlineData(1024 * 1024 + 1, false)]
    public void GlyphApcBudgetIsIndependentOfUnknownCaptureCap(int bytes, bool expected)
    {
        string input = Wire("s;" + new string('x', bytes - 2));
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        processor.Process(Encoding.ASCII.GetBytes(input));
        Assert.Equal(expected ? 1 : 0, replies.Count);
        if (NativeAvailable()) Compare(input);
    }

    [Fact]
    public void RegistrationContinuationCommitsOnceAndSessionResetClearsGlossary()
    {
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes("\u001b_25a1;r;cp=e000;" + EmptyGlyph + "\u001b"));
        Assert.Equal(1, screen.RegisteredGlyphCount);
        Assert.Equal(new byte[] { 0x1B }, processor.GetContinuation());
        processor.Process("\\"u8);
        processor.PrepareForNewSession(preserveScrollback: true);
        Assert.Equal(0, screen.RegisteredGlyphCount);
    }

    [Fact]
    public void NativeResetAndScreenLifetimeAndStoredWidthPolicyArePreserved()
    {
        if (!NativeAvailable()) return;
        Compare(Wire($"r;cp=e000;width=2;{EmptyGlyph}") + "\ue000" +
            "\u001b[?1049h" + Wire("q;cp=e000") + "\u001b[?1049l\u001b[!p" +
            Wire("q;cp=e000") + "\u001bc" + Wire("q;cp=e000"), everySplit: true, utf8: true);
        // Current libvt stores width metadata but does not use it in printCell.
        Compare(Wire($"r;cp=e000;width=2;{EmptyGlyph}") + "\ue000", everySplit: true, utf8: true);
    }

    [Fact]
    public void DisablingDuringRenderHoldDoesNotPublishPendingTextOrGlyphChanges()
    {
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;{EmptyGlyph}") + "\u001b[?2026hX"));
        processor.GlyphProtocolEnabled = false;
        Assert.Equal(0, processor.CursorCol);
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out _));
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(1, processor.CursorCol);
        Assert.Equal(0, screen.RegisteredGlyphCount);
    }

    [Fact]
    public void ScreenCopiesIsolateBothDirectionsAndShareImmutableOutlineData()
    {
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;{EmptyGlyph}")));
        TerminalScreen copy = screen.CreateStateCopy();
        Assert.True(copy.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? copied));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? original));
        Assert.Same(original, copied);
        copy.GlyphGlossary.Delete(0xE000);
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out _));
        copy.GlyphGlossary.Register(0xE001, original);
        screen.ClearRegisteredGlyphs();
        Assert.True(copy.TryGetRegisteredGlyph(0xE001, out _));
        screen.AdoptStateFrom(copy);
        Assert.True(screen.TryGetRegisteredGlyph(0xE001, out _));
    }

    private bool NativeAvailable()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native glyph protocol differential available: {available}");
        return available;
    }

    private static string Wire(string command) => "\u001b_25a1;" + command + "\u001b\\";

    private static void Compare(string input, bool everySplit = false, bool utf8 = false)
    {
        byte[] bytes = (utf8 ? Encoding.UTF8 : Encoding.Latin1).GetBytes(input);
        using GhosttyTerminal native = new(20, 4);
        native.SetGlyphProtocol(true);
        StringBuilder expected = new();
        GhosttyVtNative.GhosttyTerminalWritePtyCallback callback = (_, _, data, length) =>
        {
            byte[] response = new byte[checked((int)length)];
            Marshal.Copy(data, response, 0, response.Length);
            expected.Append(Encoding.ASCII.GetString(response));
        };
        native.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(callback));
        try
        {
            native.Write(bytes);
            for (int split = 0; split <= bytes.Length; split += everySplit ? 1 : Math.Max(1, bytes.Length / 2))
            {
                using BasicVtProcessor managed = new(new TerminalScreen(20, 4));
                StringBuilder actual = new();
                managed.ResponseCallback = data => actual.Append(Encoding.ASCII.GetString(data));
                managed.Process(bytes.AsSpan(0, split));
                managed.Process(bytes.AsSpan(split));
                Assert.Equal(expected.ToString(), actual.ToString());
                Assert.Equal((native.GetCursorX(), native.GetCursorY()), ((ushort)managed.CursorCol, (ushort)managed.CursorRow));
            }
        }
        finally { native.SetWritePtyCallback(0); GC.KeepAlive(callback); }
    }
}
