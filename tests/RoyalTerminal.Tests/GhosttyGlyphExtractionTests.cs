// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Glyphs;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyGlyphExtractionTests(ITestOutputHelper output)
{
    private const string Triangle = "AAEAZABkA4QDhAACAAABAQEB9P5wAyADhPzgAAA=";
    private const string Empty = "AAAAAAAAAAAAAA==";

    [Fact]
    public void ExtractedPointsAndMetadataMatchManagedDecoderAndSurviveDisposal()
    {
        if (!Available()) return;
        GhosttyGlyphRegistration snapshot;
        using (GhosttyTerminal terminal = new(20, 4))
        {
            Register(terminal, $"cp=e000;upm=2000;aw=1500;lh=2200;width=2;size=stretch;align=end,baseline;pad=.1,.2,0,.3", Triangle);
            snapshot = Assert.Single(terminal.GetGlyphRegistrations());
            Assert.Equal(0xE000u, snapshot.Codepoint);
            TerminalGlyphRegistration entry = snapshot.Registration;
            Assert.Equal((2000u, 1500u, 2200u, (byte)2), (entry.UnitsPerEm, entry.AdvanceWidth, entry.LineHeight, entry.Width));
            Assert.Equal(new(TerminalGlyphSize.Stretch, TerminalGlyphAlignment.End, TerminalGlyphAlignment.Start, .1, .2, 0, .3), entry.Layout);
            Register(terminal, "cp=e000", Empty);
            terminal.Write("\u001b_25a1;c\u001b\\"u8);
            Assert.Empty(terminal.GetGlyphRegistrations());
        }
        Assert.True(TerminalGlyphDecoder.TryDecode(Convert.FromBase64String(Triangle), out TerminalGlyphOutline? expected, out _));
        Assert.Equal(expected.ContourEnds.ToArray(), snapshot.Registration.Outline.ContourEnds.ToArray());
        Assert.Equal(expected.Points.ToArray(), snapshot.Registration.Outline.Points.ToArray());
    }

    [Theory]
    [InlineData("height", TerminalGlyphSize.Height)]
    [InlineData("advance", TerminalGlyphSize.Height)]
    [InlineData("contain", TerminalGlyphSize.Contain)]
    [InlineData("cover", TerminalGlyphSize.Contain)]
    [InlineData("stretch", TerminalGlyphSize.Stretch)]
    public void NativeConstraintsExposeNormalizedPolicy(string request, TerminalGlyphSize expected)
    {
        if (!Available()) return;
        using GhosttyTerminal terminal = new(20, 4);
        Register(terminal, $"cp=e000;size={request}", Triangle);
        TerminalGlyphRegistration entry = Assert.Single(terminal.GetGlyphRegistrations()).Registration;
        Assert.Equal(expected, entry.Layout.Size);
        Assert.Equal(TerminalGlyphAlignment.Center, entry.Layout.Horizontal);
        Assert.Equal(TerminalGlyphAlignment.Center, entry.Layout.Vertical);
        Assert.Equal((1000u, 1000u, 1000u, (byte)1), (entry.UnitsPerEm, entry.AdvanceWidth, entry.LineHeight, entry.Width));
    }

    [Fact]
    public void MetadataAndPointAbiLayoutAreStable()
    {
        Assert.Equal(80, Unsafe.SizeOf<GhosttyVtNative.RoyalGlyphMetadata>());
        Assert.Equal(12, Unsafe.SizeOf<GhosttyVtNative.RoyalGlyphPoint>());
        GhosttyVtNative.RoyalGlyphMetadata metadata = GhosttyVtNative.RoyalGlyphMetadata.CreateSized();
        Assert.Equal((nuint)80, metadata.Size);
        Assert.Equal((nint)48, Unsafe.ByteOffset(ref Unsafe.As<GhosttyVtNative.RoyalGlyphMetadata, byte>(ref metadata),
            ref Unsafe.As<double, byte>(ref metadata.PadTop)));
    }

    [Fact]
    public unsafe void InvalidArgumentsAndSmallBuffersNeverWritePartialOutlines()
    {
        if (!Available()) return;
        using GhosttyTerminal terminal = new(20, 4);
        uint count = 777;
        byte dirty = 77;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.GlyphGlossaryInfo(0, &count, &dirty));
        Assert.Equal(777u, count);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.GlyphGlossaryInfo(terminal.Handle, null, &dirty));
        GhosttyVtNative.RoyalGlyphMetadata metadata = GhosttyVtNative.RoyalGlyphMetadata.CreateSized();
        metadata.Codepoint = 777;
        Assert.Equal(GhosttyVtNative.GhosttyResult.NoValue, GhosttyVtNative.GlyphMetadata(terminal.Handle, 0, &metadata));
        Assert.Equal(777u, metadata.Codepoint);
        Register(terminal, "cp=e000", Triangle);
        metadata.Size = 1;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.GlyphMetadata(terminal.Handle, 0, &metadata));
        Assert.Equal(777u, metadata.Codepoint);
        metadata = GhosttyVtNative.RoyalGlyphMetadata.CreateSized();
        Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.GlyphMetadata(terminal.Handle, 0, &metadata));
        Assert.Equal((3u, 1u), (metadata.PointCount, metadata.ContourCount));
        Span<GhosttyVtNative.RoyalGlyphPoint> points = stackalloc GhosttyVtNative.RoyalGlyphPoint[3];
        points.Fill(new() { X = 777, Y = 888, OnCurve = 999 });
        ushort contour = 777;
        fixed (GhosttyVtNative.RoyalGlyphPoint* pointer = points)
        {
            Assert.Equal(GhosttyVtNative.GhosttyResult.OutOfSpace, GhosttyVtNative.GlyphOutline(terminal.Handle, 0, pointer, 2, &contour, 1));
            Assert.Equal(GhosttyVtNative.GhosttyResult.OutOfSpace, GhosttyVtNative.GlyphOutline(terminal.Handle, 0, pointer, 3, &contour, 0));
            Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.GlyphOutline(terminal.Handle, 0, pointer, 3, null, 1));
            Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.GlyphOutline(terminal.Handle, 0, null, 3, &contour, 1));
            foreach (GhosttyVtNative.RoyalGlyphPoint point in points) Assert.Equal((777, 888, 999u), (point.X, point.Y, point.OnCurve));
            Assert.Equal((ushort)777, contour);
            Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.GlyphOutline(terminal.Handle, 0, pointer, 3, &contour, 1));
        }
        Assert.Equal((ushort)2, contour);
        Assert.Equal((500, 900, 1u), (points[0].X, points[0].Y, points[0].OnCurve));
        Register(terminal, "cp=e000", Empty);
        Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.GlyphOutline(terminal.Handle, 0, null, 0, null, 0));
        Assert.Equal(GhosttyVtNative.GhosttyResult.NoValue, GhosttyVtNative.GlyphOutline(terminal.Handle, 1, null, 0, null, 0));
    }

    [Fact]
    public void FifoOrderAndDirtyPeekArePreservedWithoutAllocatingOrClearing()
    {
        if (!Available()) return;
        using GhosttyTerminal terminal = new(20, 4);
        Register(terminal, "cp=e000", Triangle);
        Register(terminal, "cp=e001", Empty);
        Register(terminal, "cp=e000", Triangle);
        GhosttyGlyphRegistration[] entries = terminal.GetGlyphRegistrations();
        Assert.Equal(0xE001u, entries[0].Codepoint);
        Assert.Equal(0xE000u, entries[1].Codepoint);
        terminal.GetGlyphGlossaryInfo(out uint count, out bool dirty);
        Assert.Equal(2u, count);
        Assert.True(dirty);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) terminal.GetGlyphGlossaryInfo(out count, out dirty);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(dirty);
        using GhosttyRenderState state = new();
        state.Update(terminal);
        terminal.GetGlyphGlossaryInfo(out count, out dirty);
        Assert.False(dirty);
        terminal.SetGlyphProtocol(false);
        terminal.GetGlyphGlossaryInfo(out count, out _);
        Assert.Equal(0u, count);
    }

    [Fact]
    public void NativeProcessorPublishesGlyphsAndKeepsUnchangedEntryIdentity()
    {
        if (!Available()) return;
        TerminalScreen screen = new(20, 4);
        using GhosttyVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(Wire("r;cp=e000;" + Triangle)));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? original));
        processor.Process("X\u001b[0m"u8);
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? unchanged));
        Assert.Same(original, unchanged);
        processor.Process(Encoding.ASCII.GetBytes(Wire("r;cp=e000;" + Empty)));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out TerminalGlyphRegistration? replacement));
        Assert.NotSame(original, replacement);
        Assert.True(replacement.Outline.Points.IsEmpty);
        processor.Process("\u001bc"u8);
        Assert.Equal(0, screen.RegisteredGlyphCount);
    }

    [Fact]
    public void NativeProcessorFreezesGlyphPublicationButNotQueryStateDuringHold()
    {
        if (!Available()) return;
        TerminalScreen screen = new(20, 4);
        using GhosttyVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(Wire("r;cp=e000;" + Triangle) + "\u001b[?2026h" + Wire("c") + Wire("r;cp=e001;" + Empty)));
        Assert.True(screen.TryGetRegisteredGlyph(0xE000, out _));
        Assert.False(screen.TryGetRegisteredGlyph(0xE001, out _));
        string? reply = null;
        processor.ResponseCallback = data => reply = Encoding.ASCII.GetString(data);
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=e001")));
        Assert.Contains("status=glossary", reply);
        processor.Process("\u001b[?2026l"u8);
        Assert.False(screen.TryGetRegisteredGlyph(0xE000, out _));
        Assert.True(screen.TryGetRegisteredGlyph(0xE001, out _));
    }

    [Fact]
    public void CopiedWrapperApisRejectDisposedTerminals()
    {
        if (!Available()) return;
        GhosttyTerminal terminal = new(20, 4);
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.GetGlyphGlossaryInfo(out _, out _));
        Assert.Throws<ObjectDisposedException>(() => terminal.GetGlyphRegistrations());
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native glyph extraction available: {available}");
        return available;
    }

    private static string Wire(string content) => "\u001b_25a1;" + content + "\u001b\\";
    private static void Register(GhosttyTerminal terminal, string options, string payload)
        => terminal.Write(Encoding.ASCII.GetBytes(Wire("r;" + options + ";" + payload)));
}
