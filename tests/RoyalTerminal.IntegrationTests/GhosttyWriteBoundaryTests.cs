// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

// These are the public C API contracts in Ghostty 4a4fc0179 terminal.h and
// terminal/c/terminal.zig. The API exposes Ghostty's actual parser boundaries;
// it does not introduce an independent parser or choose new terminal semantics.
public sealed class GhosttyWriteBoundaryTests
{
    [GhosttyNativeFact]
    public void StopsBeforeOscContentAndDoesNotProcessTheUnconsumedSuffix()
    {
        using GhosttyTerminal terminal = new(20, 4);
        ReadOnlySpan<byte> input = "\x1b]2;first\a\x1b]2;second\a"u8;
        Assert.True(terminal.WriteUntil(input, GhosttyTerminalBoundary.OscStart | GhosttyTerminalBoundary.OscEnd, out var start));
        Assert.Equal((nuint)2, start.Consumed);
        Assert.Equal((uint)GhosttyTerminalBoundary.OscStart, start.Reached);
        Assert.Equal(GhosttyTerminalStringKind.Osc, start.Kind);
        Assert.Equal(GhosttyTerminalStringIntroducer.Esc, start.Introducer);
        Assert.Equal((byte)']', start.Byte);

        Assert.True(terminal.WriteUntil(input[(int)start.Consumed..], GhosttyTerminalBoundary.OscEnd | GhosttyTerminalBoundary.Ground, out var end));
        Assert.Equal((nuint)8, end.Consumed);
        Assert.Equal((uint)(GhosttyTerminalBoundary.OscEnd | GhosttyTerminalBoundary.Ground), end.Reached);
        Assert.Equal(GhosttyTerminalStringEnd.Bel, end.End);
        Assert.Equal("first", terminal.GetTitle());
        terminal.Write(input[(int)(start.Consumed + end.Consumed)..]);
        Assert.Equal("second", terminal.GetTitle());
    }

    [GhosttyNativeFact]
    public void SplitIntroducerAndEscTerminatorPreserveUnconsumedBytes()
    {
        using GhosttyTerminal terminal = new(20, 4);
        Assert.False(terminal.WriteUntil("\x1b"u8, GhosttyTerminalBoundary.ApcStart, out var partial));
        Assert.Equal((nuint)1, partial.Consumed);
        Assert.Equal(0u, partial.Reached);
        Assert.True(terminal.WriteUntil("_ignored\x1b\\tail"u8, GhosttyTerminalBoundary.ApcStart, out var start));
        Assert.Equal((nuint)1, start.Consumed);
        Assert.True(terminal.WriteUntil("ignored\x1b\\tail"u8, GhosttyTerminalBoundary.ApcEnd, out var end));
        Assert.Equal((nuint)8, end.Consumed);
        Assert.Equal(GhosttyTerminalStringEnd.StEsc, end.End);
        Assert.Equal((byte)0x1b, end.Byte);
        Assert.True(terminal.WriteUntilGround("\\tail"u8, out nuint consumed));
        Assert.Equal((nuint)1, consumed);
        Assert.True(terminal.WriteUntilGround("tail"u8, out consumed));
        Assert.Equal((nuint)0, consumed);
    }

    [GhosttyNativeFact]
    public void OneC1ByteCanEndApcAndStartOsc()
    {
        using GhosttyTerminal terminal = new(20, 4);
        terminal.Write("\x1b_a"u8);
        Assert.True(terminal.WriteUntil(new byte[] { 0x9d, (byte)'2', (byte)';', (byte)'t', 7 },
            GhosttyTerminalBoundary.ApcEnd | GhosttyTerminalBoundary.OscStart, out var stop));
        Assert.Equal((nuint)1, stop.Consumed);
        Assert.Equal((uint)(GhosttyTerminalBoundary.ApcEnd | GhosttyTerminalBoundary.OscStart), stop.Reached);
        Assert.Equal(GhosttyTerminalStringKind.Apc, stop.Kind);
        Assert.Equal(GhosttyTerminalStringIntroducer.C1, stop.Introducer);
        Assert.Equal(GhosttyTerminalStringEnd.Aborted, stop.End);
        terminal.Write("2;t\a"u8);
        Assert.Equal("t", terminal.GetTitle());
    }

    [GhosttyNativeFact]
    public void InvalidMasksAndDisposedWritesAreRejected()
    {
        using GhosttyTerminal terminal = new(20, 4);
        Assert.Throws<InvalidOperationException>(() => terminal.WriteUntil("text"u8, 0, out _));
        Assert.Throws<InvalidOperationException>(() => terminal.WriteUntil("text"u8, unchecked((GhosttyTerminalBoundary)0x80000000u), out _));
        Assert.Equal((ushort)0, terminal.GetCursorX());
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.WriteUntil("text"u8, GhosttyTerminalBoundary.OscStart, out _));
    }

    [GhosttyNativeFact]
    public void PromptViewportDeltaUsesPromptGroupsAndClamps()
    {
        using GhosttyTerminal terminal = new(80, 2);
        terminal.Write(("\x1b]133;P;k=i\ap1\r\n\x1b]133;B\acmd1\r\n\x1b]133;C\aout1\r\n" +
            "\x1b]133;P;k=i\ap2\r\n\x1b]133;B\acmd2\r\n\x1b]133;C\aout2\r\n" +
            "\x1b]133;P;k=i\ap3\r\n\x1b]133;B\acmd3\r\n\x1b]133;C\aout3\r\n" +
            "\x1b]133;P;k=i\ap4").Select(c => (byte)c).ToArray());
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.DeltaPrompts(-1));
        Assert.Equal(6ul, terminal.GetScrollbar().Offset);
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.DeltaPrompts(-1));
        Assert.Equal(3ul, terminal.GetScrollbar().Offset);
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.DeltaPrompts(1));
        Assert.Equal(6ul, terminal.GetScrollbar().Offset);
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.DeltaPrompts(nint.MinValue));
        Assert.Equal(0ul, terminal.GetScrollbar().Offset);
    }

    [GhosttyNativeFact]
    public void Csi19SizeReportUsesScreenReportNumber()
    {
        Assert.Equal("\x1b[9;24;80t", GhosttyVtHelpers.EncodeSizeReportString(
            GhosttySizeReportStyle.Csi19T, new() { Rows = 24, Columns = 80 }));
    }
}
