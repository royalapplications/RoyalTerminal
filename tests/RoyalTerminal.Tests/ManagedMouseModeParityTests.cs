// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedMouseModeParityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Pairs()
    {
        int[] modes = [9, 1000, 1002, 1003, 1005, 1006, 1015, 1016];
        foreach (int first in modes) foreach (int second in modes) yield return [first, second];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void MixedModesKeepIndependentBitsButLastCommandDeterminesBehavior(int first, int second)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native mouse transition differential available: {available}");
        if (!available) return;
        using GhosttyTerminal native = new(8, 3);
        using GhosttyVtProcessor adapter = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        TerminalMouseModeTracker tracker = new();
        string[] commands = [$"\u001b[?{first}h", $"\u001b[?{second}h", $"\u001b[?{first}s", $"\u001b[?{second}s",
            $"\u001b[?{first}l", $"\u001b[?{second}l", $"\u001b[?{first}r", $"\u001b[?{second}r",
            $"\u001b[?{first}h", $"\u001b[?{second}l", "\u001bc", $"\u001b[?{first}r"];
        foreach (string command in commands)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes);
            adapter.Process(bytes);
            // Feed both consumers bytewise so the UI fallback and the processor
            // preserve ordering across every possible control-sequence boundary.
            foreach (byte value in bytes)
            {
                managed.Process(new ReadOnlySpan<byte>(in value));
                tracker.Process(new ReadOnlySpan<byte>(in value));
            }
            GhosttySnapshotTerminalHeader expected = ManagedSnapshotModeInstallationTests.NativeHeader(native);
            TerminalMouseModeState state = new((TerminalMouseTrackingMode)expected.MouseEvent, (TerminalMouseEncoding)expected.MouseFormat);
            Assert.Equal(state, managed.SnapshotMouseModes);
            Assert.Equal(state, managed.MouseModeState);
            Assert.Equal(state, adapter.MouseModeState);
            Assert.Equal(state, native.GetMouseModeState());
            Assert.Equal(state.IsMouseReportingEnabled, adapter.MouseReportingEnabled);
            Assert.Equal(state, tracker.ModeState);
            Assert.Equal(expected.CurrentModes, managed.SnapshotCurrentModes);
            Assert.Equal(expected.SavedModes, managed.SnapshotSavedModes);
            Assert.Equal(expected.MouseEvent != 0, managed.MouseReportingEnabled);
        }
    }

    [Theory]
    [InlineData("\u001b[?1003h\u001b[?1000h\u001b[?1006h")]
    [InlineData("\u001b[?1003h\u001b[?9l\u001b[?1006h")]
    [InlineData("\u001b[?1000h\u001b[?1016h\u001b[?1006h")]
    [InlineData("\u001b[?1000h\u001b[?1006h\u001b[?1005l")]
    [InlineData("\u001b[?1000h\u001b[?1016h\u001b[?1015h")]
    public void PointerEncodingUsesTheEffectiveState(string input)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        byte[] bytes = Encoding.ASCII.GetBytes(input);
        native.Process(bytes); managed.Process(bytes);
        TerminalPointerEvent pointer = new(Kind: TerminalPointerEventKind.Button, X: 11, Y: 12,
            Button: TerminalMouseButton.Left, Action: TerminalInputAction.Press, Modifiers: TerminalModifiers.None);
        TerminalPointerEncodingContext context = new(ScreenWidthPx: 80, ScreenHeightPx: 60, CellWidthPx: 10, CellHeightPx: 20);
        Assert.Equal(native.TryEncodePointer(pointer, context, out byte[] expected), managed.TryEncodePointer(pointer, context, out byte[] actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TransitionsAndTrackerReadsAllocateNothingAfterWarmup()
    {
        TerminalMouseModeTracker tracker = new();
        ReadOnlySpan<byte> input = "\u001b[?1003;1016h\u001b[?9;1006h\u001b[?1003;1016l\u001b[?9;1006s\u001b[?9;1006r"u8;
        for (int i = 0; i < 1000; i++) tracker.Process(input);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { tracker.Process(input); _ = tracker.ModeState; }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(default, TerminalMouseModeTransitions.Apply(default, 65535, true));
    }
}
