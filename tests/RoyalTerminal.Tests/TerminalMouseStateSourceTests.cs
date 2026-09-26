// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalMouseStateSourceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> States()
    {
        for (byte tracking = 0; tracking <= 4; tracking++)
        for (byte format = 0; format <= 4; format++) yield return [tracking, format];
    }

    [Theory]
    [MemberData(nameof(States))]
    public void RestoredFlagsAreIndependentOfProtocolBits(byte tracking, byte format)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[0].Payload[34] = tracking; records[0].Payload[35] = format;
        for (int i = 0; i < records.Count; i++)
            if (records[i].Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(records[i].Tag, []);
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        using BasicVtProcessor managed = new(new TerminalScreen(2, 3));
        managed.InstallSnapshotModes(ready.Terminal.Header);
        TerminalMouseModeState expected = new((TerminalMouseTrackingMode)tracking, (TerminalMouseEncoding)format);
        Assert.Equal(expected, native.GetMouseModeState());
        Assert.Equal(expected, ((ITerminalMouseModeStateSource)managed).MouseModeState);
        Assert.Equal(tracking != 0, managed.MouseReportingEnabled);
    }

    [Fact]
    public void NativeEffectiveReportingCanBeOffWhileRawModeBitsRemainSet()
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 3);
        using GhosttyVtProcessor adapter = new(new TerminalScreen(8, 3));
        native.Write("\u001b[?1003h\u001b[?9l"u8); adapter.Process("\u001b[?1003h\u001b[?9l"u8);
        Assert.True(native.GetMouseTracking()); // Public upstream query is an OR of mode bits.
        Assert.False(native.GetMouseModeState().IsMouseReportingEnabled);
        Assert.False(adapter.MouseReportingEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputStateIsLiveDuringRenderHoldAndClearedOnSessionReset(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalMouseModeStateSource source = (ITerminalMouseModeStateSource)processor;
        processor.Process("\u001b[?2026h\u001b[?1003;1016h"u8);
        Assert.Equal(new(TerminalMouseTrackingMode.AnyMotion, TerminalMouseEncoding.SgrPixels), source.MouseModeState);
        Assert.True(source.MouseReportingEnabled);
        processor.Process("\u001b[?9l"u8); Assert.False(source.MouseReportingEnabled);
        foreach (bool preserve in new[] { true, false })
        {
            processor.Process("\u001b[?1003;1016h"u8);
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserve);
            Assert.Equal(default, source.MouseModeState); Assert.False(source.MouseReportingEnabled);
        }
    }

    [Fact]
    public unsafe void NativeStateAbiGuardsAndWarmCopies()
    {
        Assert.Equal(IntPtr.Size == 8 ? 24 : 20, Unsafe.SizeOf<GhosttyVtNative.RoyalMouseState>());
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 3);
        GhosttyVtNative.RoyalMouseState state = GhosttyVtNative.RoyalMouseState.CreateSized();
        state.Tracking = 77; state.Format = 88;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.MouseState(0, &state));
        Assert.Equal((77U, 88U), (state.Tracking, state.Format));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.MouseState(native.Handle, null));
        state.Size--;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.MouseState(native.Handle, &state));
        Assert.Equal((77U, 88U), (state.Tracking, state.Format));
        native.Write("\u001b[?1003;1016h"u8);
        for (int i = 0; i < 1000; i++) _ = native.GetMouseModeState();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = native.GetMouseModeState();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        native.Dispose(); Assert.Throws<ObjectDisposedException>(() => native.GetMouseModeState());
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native effective mouse state available: {available}"); return available;
    }
}
