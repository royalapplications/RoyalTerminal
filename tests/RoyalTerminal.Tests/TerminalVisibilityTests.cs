// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #13494: host visibility survives reset, is separate from focus, and
// emits on query, every enable, and enabled transitions (not duplicate updates).
public sealed class TerminalVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueriesEnablesAndTransitionsUseLiveHostState(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalVisibilityState state = Assert.IsAssignableFrom<ITerminalVisibilityState>(processor);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Assert.True(state.PotentiallyVisible);
        processor.Process("\u001b[?998n"u8);
        Assert.Equal([Visible], replies);
        replies.Clear();
        state.PotentiallyVisible = false;
        Assert.Empty(replies);
        processor.Process("\u001b[?998n\u001b[?2033h\u001b[?2033h"u8);
        Assert.Equal([Hidden, Hidden, Hidden], replies);
        replies.Clear();
        state.PotentiallyVisible = false;
        state.PotentiallyVisible = true;
        state.PotentiallyVisible = true;
        state.PotentiallyVisible = false;
        Assert.Equal([Visible, Hidden], replies);
        replies.Clear();
        processor.Process("\u001b[?2033l"u8);
        state.PotentiallyVisible = true;
        Assert.Empty(replies);
        processor.Process("\u001b[?998n"u8);
        Assert.Equal([Visible], replies);
    }

    [Theory]
    [InlineData(false, "ris")]
    [InlineData(true, "ris")]
    [InlineData(false, "api")]
    [InlineData(true, "api")]
    [InlineData(false, "session-clear")]
    [InlineData(true, "session-clear")]
    [InlineData(false, "session-preserve")]
    [InlineData(true, "session-preserve")]
    [InlineData(false, "soft")]
    [InlineData(true, "soft")]
    public void ResetsAndAlternateScreensDoNotInventVisibility(bool native, string reset)
    {
        using IVtProcessor processor = Create(native);
        ITerminalVisibilityState state = (ITerminalVisibilityState)processor;
        state.PotentiallyVisible = false;
        processor.Process("\u001b[?2033h\u001b[?1049h\u001b[?1049l"u8);
        switch (reset)
        {
            case "ris": processor.Process("\u001bc"u8); break;
            case "api": processor.Reset(); break;
            case "soft": processor.Process("\u001b[!p"u8); break;
            default: ((ITerminalSessionHistoryController)processor).PrepareForNewSession(reset == "session-preserve"); break;
        }
        Assert.False(state.PotentiallyVisible);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.Process("\u001b[?998n"u8);
        Assert.Equal([Hidden], replies);
        replies.Clear();
        if (reset != "soft")
        {
            state.PotentiallyVisible = true;
            Assert.Empty(replies); // Reset disabled reports, not host metadata.
            processor.Process("\u001b[?2033h"u8);
            Assert.Equal([Visible], replies);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostEventsDoNotCorruptPartialInputOrWaitForSynchronizedOutput(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalVisibilityState state = (ITerminalVisibilityState)processor;
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        string? title = null;
        processor.TitleCallback = value => title = value;
        processor.Process("\u001b[?203"u8);
        state.PotentiallyVisible = false;
        processor.Process("3h\u001b]2;unchanged"u8);
        state.PotentiallyVisible = true;
        processor.Process(" title\a\u001b[?2026h"u8);
        Assert.Equal("unchanged title", title);
        state.PotentiallyVisible = false;
        processor.Process("\u001b[?998n"u8);
        Assert.Equal([Hidden, Visible, Hidden, Hidden], replies);
        processor.Process("\u001b[?2026l"u8);
        Assert.False(state.PotentiallyVisible);
    }

    [Fact]
    public void SnapshotTransfersModesButNotTheSourceHostsVisibility()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 3));
        processor.Process("\u001b[?2033h"u8);
        processor.PotentiallyVisible = false;
        byte[] snapshot = processor.GetBinarySnapshot();
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(snapshot);
        Assert.True(restored.Processor.PotentiallyVisible);
        byte[]? response = null;
        restored.Processor.ResponseCallback = bytes => response = bytes;
        restored.Processor.PotentiallyVisible = false;
        Assert.Equal(Hidden, Encoding.ASCII.GetString(response!));
        Assert.False(processor.PotentiallyVisible);
        RequireNative();
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        Assert.True(native.PotentiallyVisible);
        Assert.Equal(Hidden, Encoding.ASCII.GetString(native.SetVisibility(false)));
        using GhosttyTerminal nativeRestored = GhosttySnapshot.Decode(GhosttySnapshot.Encode(native));
        Assert.True(nativeRestored.PotentiallyVisible);
        Assert.False(native.PotentiallyVisible);
    }

    [Fact]
    public unsafe void NativeAbiValidatesBeforeMutationAndReturnsOwnedReportBytes()
    {
        RequireNative();
        using GhosttyTerminal terminal = new(8, 3);
        byte value = 77;
        nuint written = 123;
        byte* buffer = stackalloc byte[10];
        new Span<byte>(buffer, 10).Fill(0xCC);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.VisibilityGet(0, &value));
        Assert.Equal(77, value);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.VisibilityGet(terminal.Handle, null));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.VisibilitySet(0, 0, buffer, 9, &written));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.VisibilitySet(terminal.Handle, 2, buffer, 9, &written));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.VisibilitySet(terminal.Handle, 0, buffer, 9, null));
        Assert.Equal((nuint)123, written);
        Assert.True(terminal.PotentiallyVisible);
        terminal.Write("\u001b[?2033h"u8);
        Assert.Equal(GhosttyVtNative.GhosttyResult.OutOfSpace, GhosttyVtNative.VisibilitySet(terminal.Handle, 0, buffer, 8, &written));
        Assert.Equal((nuint)9, written);
        Assert.True(terminal.PotentiallyVisible);
        Assert.All(new ReadOnlySpan<byte>(buffer, 10).ToArray(), b => Assert.Equal(0xCC, b));
        written = 123;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.VisibilitySet(terminal.Handle, 0, null, 9, &written));
        Assert.Equal((nuint)123, written);
        Assert.True(terminal.PotentiallyVisible);
        Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.VisibilitySet(terminal.Handle, 0, buffer, 9, &written));
        Assert.Equal(Hidden, Encoding.ASCII.GetString(new ReadOnlySpan<byte>(buffer, (int)written)));
        Assert.Equal(0xCC, buffer[9]);
        Assert.False(terminal.PotentiallyVisible);
        Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.VisibilitySet(terminal.Handle, 0, null, 0, &written));
        Assert.Equal((nuint)0, written);
        byte[] owned = terminal.SetVisibility(true);
        terminal.SetVisibility(false);
        Assert.Equal(Visible, Encoding.ASCII.GetString(owned));
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.PotentiallyVisible);
        Assert.Throws<ObjectDisposedException>(() => terminal.SetVisibility(false));
    }

    private static IVtProcessor Create(bool native)
    {
        if (native) RequireNative();
        return native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
    }

    private static void RequireNative()
    {
        if (!GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native Ghostty is unavailable.");
    }

    private const string Visible = "\u001b[?999;1n";
    private const string Hidden = "\u001b[?999;2n";
}
