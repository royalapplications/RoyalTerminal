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

public sealed class TerminalMouseShiftCaptureTests(ITestOutputHelper output)
{
    public static IEnumerable<object?[]> Policies()
    {
        foreach (TerminalMouseShiftCapturePolicy policy in Enum.GetValues<TerminalMouseShiftCapturePolicy>())
        foreach (bool? application in new bool?[] { null, false, true })
            yield return [policy, application, policy switch
            {
                TerminalMouseShiftCapturePolicy.Never => false,
                TerminalMouseShiftCapturePolicy.Always => true,
                _ => application ?? policy == TerminalMouseShiftCapturePolicy.Enabled,
            }];
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void HostPolicyMatchesGhostty(TerminalMouseShiftCapturePolicy policy, bool? application, bool expected)
        => Assert.Equal(expected, TerminalMouseCapturePolicy.IsShiftCaptured(policy, application));

    [Fact]
    public void InvalidHostPolicyIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TerminalMouseCapturePolicy.IsShiftCaptured((TerminalMouseShiftCapturePolicy)99, null));

    [Theory]
    [InlineData("\u001b[>s", false)]
    [InlineData("\u001b[>0s", false)]
    [InlineData("\u001b[>1s", true)]
    [InlineData("\u001b[>2s", null)]
    [InlineData("\u001b[>1;0s", null)]
    [InlineData("\u001b[>1:0s", null)]
    [InlineData("\u001b[>1 s", null)]
    public void XtsShiftEscapeMatchesNativeAcrossEverySplit(string sequence, bool? requested)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);
        foreach (bool? initial in new bool?[] { null, false, true })
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyTerminal native = new(8, 3);
            using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
            native.SetMouseShiftCapture(initial); managed.MouseShiftCaptureOverride = initial;
            native.Write(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(0, split));
            native.Write(bytes.AsSpan(split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(requested ?? initial, native.GetMouseInputState().ShiftCaptureOverride);
            Assert.Equal(native.GetMouseInputState().ShiftCaptureOverride, managed.MouseShiftCaptureOverride);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StateIsGlobalLiveDuringHoldAndSessionScoped(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalMouseShiftCaptureState state = (ITerminalMouseShiftCaptureState)processor;
        Assert.Null(state.MouseShiftCaptureOverride);
        processor.Process("\u001b[?2026h\u001b[>1s"u8);
        Assert.True(state.MouseShiftCaptureOverride);
        processor.Process("\u001b[?1049h\u001b[!p\u001b[?1049l"u8);
        Assert.True(state.MouseShiftCaptureOverride);
        state.MouseShiftCaptureOverride = null;
        Assert.Null(state.MouseShiftCaptureOverride);
        processor.Process("\u001b[>0s"u8);
        Assert.False(state.MouseShiftCaptureOverride);
        processor.Process("\u001bc"u8);
        Assert.Null(state.MouseShiftCaptureOverride);
        foreach (bool preserve in new[] { true, false })
        {
            processor.Process("\u001b[>1s"u8);
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserve);
            Assert.Null(state.MouseShiftCaptureOverride);
        }
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void SnapshotInstallsNullableOverride(int wire, bool? expected)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[0].Payload[36] = (byte)wire;
        for (int i = 0; i < records.Count; i++)
            if (records[i].Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(records[i].Tag, []);
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        using BasicVtProcessor managed = new(new TerminalScreen(2, 3));
        managed.InstallSnapshotModes(reader.ReadReady().Terminal.Header);
        Assert.Equal(expected, native.GetMouseInputState().ShiftCaptureOverride);
        Assert.Equal(expected, managed.MouseShiftCaptureOverride);
    }

    [Fact]
    public void NativeSetterGuardsAndWarmCopies()
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 3);
        native.SetMouseShiftCapture(true);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.MouseShiftCaptureSet(0, 2));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.MouseShiftCaptureSet(native.Handle, 3));
        Assert.True(native.GetMouseInputState().ShiftCaptureOverride);
        for (int i = 0; i < 1000; i++) { native.SetMouseShiftCapture(null); _ = native.GetMouseInputState(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { native.SetMouseShiftCapture(null); _ = native.GetMouseInputState(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        native.Dispose();
        Assert.Throws<ObjectDisposedException>(() => native.GetMouseInputState());
        Assert.Throws<ObjectDisposedException>(() => native.SetMouseShiftCapture(null));
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native Shift capture available: {available}");
        return available;
    }
}
