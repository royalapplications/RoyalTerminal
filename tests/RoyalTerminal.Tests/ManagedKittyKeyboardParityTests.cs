// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyKeyboardParityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Commands()
    {
        string[] commands = [">u", ">0u", ">31u", ">32u", ">63u", ">65535u", ">1;2u", ">;u",
            "<u", "<0u", "<1u", "<7u", "<8u", "<9u", "<65535u", "<2;3u",
            "=u", "=1u", "=31u", "=32u", "=65535u", "=1;0u", "=1;1u", "=1;2u", "=1;3u", "=1;4u",
            "=1;65535u", "=1;2;3u", "=;2u", "=1;u", ">1:2u", "<1:2u", "=1:2u"];
        foreach (string command in commands) yield return ["\u001b[" + command];
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void CommandDefaultsArityAndBoundsMatchNativeAtEverySplit(string command)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.ASCII.GetBytes(command);
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyTerminal native = new(8, 2);
            using BasicVtProcessor managed = new(new TerminalScreen(8, 2));
            native.Write("\u001b[=7u\u001b[>11u"u8); managed.Process("\u001b[=7u\u001b[>11u"u8);
            native.Write(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(0, split));
            native.Write(bytes.AsSpan(split)); managed.Process(bytes.AsSpan(split));
            Compare(native, managed);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(32)]
    [InlineData(40)]
    public void RingOverflowAndPoppedSlotsMatchEveryNativeScreenSlot(int pushes)
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 2);
        using BasicVtProcessor managed = new(new TerminalScreen(8, 2));
        for (int i = 0; i < pushes; i++) Apply($"\u001b[>{i % 32}u");
        for (int i = 0; i < 10; i++) Apply("\u001b[<u");
        Apply("\u001b[=31u"); Apply("\u001b[<65535u");
        void Apply(string command)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes); managed.Process(bytes); Compare(native, managed);
        }
    }

    [Fact]
    public void ScreensRetainIndependentRingsAcrossSwitchesHoldsAndRis()
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 2);
        using BasicVtProcessor managed = new(new TerminalScreen(8, 2));
        string[] commands = ["\u001b[>3u", "\u001b[>7u", "\u001b[?1049h", "\u001b[>11u",
            "\u001b[?2026h", "\u001b[>13u", "\u001b[<0u", "\u001b[?2026l", "\u001b[?1049l",
            "\u001b[<u", "\u001b[?47h", "\u001b[<u", "\u001b[?47l", "\u001bc", "\u001b[?47h"];
        foreach (string command in commands)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes); managed.Process(bytes); Compare(native, managed);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void InstallingAllSnapshotSlotsPreservesSubsequentNativeOperations(int index)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        foreach (SnapshotTestRecord record in records)
        {
            if (record.Tag != GhosttySnapshotRecordTag.Screen) continue;
            record.Payload[41] = (byte)index;
            for (int slot = 0; slot < 8; slot++) record.Payload[42 + slot] = (byte)(slot * 3 + record.Payload[0]);
        }
        byte[] source = SnapshotTestRecords.Encode(records);
        using GhosttyTerminal native = GhosttySnapshot.Decode(source);
        using GhosttySnapshotStateReader reader = new(source, new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        using BasicVtProcessor managed = new(new TerminalScreen(2, 3));
        foreach (GhosttySnapshotScreen screen in ready.Screens) managed.InstallSnapshotKittyKeyboard(screen.State);
        // Match the active screen without constructing the unrelated processor state.
        if (ready.Terminal.Header.ActiveScreenKey == 1) managed.Process("\u001b[?47h"u8);
        Compare(native, managed);
        string[] commands = ["\u001b[<0u", "\u001b[<2u", "\u001b[>31u", "\u001b[=1;3u",
            "\u001b[?47h", "\u001b[<7u", "\u001b[?47l", "\u001b[<8u"];
        foreach (string command in commands)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes); managed.Process(bytes); Compare(native, managed);
        }
    }

    [Fact]
    public void PackedStateValidatesSnapshotBoundariesAndAllocatesNothing()
    {
        Assert.Equal(8, Unsafe.SizeOf<ManagedKittyKeyboardState>());
        byte[] flags = [1, 2, 4, 8, 16, 31, 0, 17];
        ManagedKittyKeyboardState state = ManagedKittyKeyboardState.FromSnapshot(7, flags);
        Span<byte> copy = stackalloc byte[8]; state.CopyFlagsTo(copy);
        Assert.Equal(flags, copy.ToArray());
        Assert.Equal(17, state.Current);
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedKittyKeyboardState.FromSnapshot(8, flags));
        Assert.Throws<ArgumentException>(() => ManagedKittyKeyboardState.FromSnapshot(0, [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManagedKittyKeyboardState.FromSnapshot(0, [32, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Push(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Set(32));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Pop(-1));
        Assert.Throws<ArgumentException>(() => state.CopyFlagsTo(new byte[7]));
        for (int i = 0; i < 1000; i++) { state.Push(31); state.Pop(1); state.CopyFlagsTo(copy); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { state.Push(31); state.Pop(1); state.CopyFlagsTo(copy); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native Kitty keyboard differential available: {available}");
        return available;
    }

    private static void Compare(GhosttyTerminal native, BasicVtProcessor managed)
    {
        Assert.Equal((int)native.GetKittyKeyboardFlags(), managed.KittyKeyboardFlags);
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        Span<byte> flags = stackalloc byte[8];
        foreach (GhosttySnapshotScreen screen in ready.Screens)
        {
            ManagedKittyKeyboardState actual = managed.GetSnapshotKittyKeyboard(screen.State.Key);
            Assert.Equal(screen.State.KittyKeyboardIndex, actual.Index);
            actual.CopyFlagsTo(flags);
            Assert.Equal(screen.State.KittyKeyboardFlags.ToArray(), flags.ToArray());
        }
    }
}
