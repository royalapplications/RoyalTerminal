// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSavedModeParityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Modes()
    {
        int[] modes = [1, 3, 4, 5, 6, 7, 8, 9, 12, 25, 40, 45, 47, 66, 67, 69,
            1000, 1002, 1003, 1004, 1005, 1006, 1007, 1015, 1016, 1035, 1036,
            1039, 1045, 1047, 1048, 1049, 2004, 2026, 2027, 2031, 2033, 2048, 5522];
        foreach (int mode in modes) yield return [mode];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EverySavedModeMatchesNativeSnapshotBanksAndRepeatedRestore(int mode)
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(12, 4);
        using BasicVtProcessor managed = new(new TerminalScreen(12, 4));
        string[] operations = ["", $"\u001b[?{mode}l", $"\u001b[?{mode}r", // Restore without save uses initial bank.
            $"\u001b[?{mode}h", $"\u001b[?{mode}s", $"\u001b[?{mode}l", $"\u001b[?{mode}r",
            $"\u001b[?{mode}l", $"\u001b[?{mode}r", // Saved value is retained, not popped.
            $"\u001b[?{mode}l", $"\u001b[?{mode}s", $"\u001b[?{mode}h", $"\u001b[?{mode}r",
            "\u001bc", $"\u001b[?{mode}r"];
        foreach (string operation in operations)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(operation);
            native.Write(bytes); managed.Process(bytes);
            GhosttySnapshotTerminalHeader header = Header(native);
            Assert.Equal(header.CurrentModes, managed.SnapshotCurrentModes);
            Assert.Equal(header.SavedModes, managed.SnapshotSavedModes);
            Assert.Equal(BasicVtProcessor.SnapshotInitialModes, header.DefaultModes);
        }
    }

    [Theory]
    [InlineData("\u001b[?47h\u001b[?1049s\u001b[?47l\u001b[?1049r")]
    [InlineData("\u001b[?1049h\u001b[?47s\u001b[?47l\u001b[?47r")]
    [InlineData("\u001b[?47;1047;1049h\u001b[?47;1047;1049s\u001b[?1049l\u001b[?47;1047;1049r")]
    [InlineData("\u001b[?7;25;1007l\u001b[?7;25;1007s\u001b[?7;25;1007h\u001b[?1007;7;25r")]
    [InlineData("\u001b[?7;65535;25s\u001b[?7;25l\u001b[?0;65535;7;25r")]
    [InlineData("\u001b[?7;7s\u001b[?7l\u001b[?7;7r")]
    public void MixedModeBanksAndModeQueriesMatchAtEveryInputSplit(string input)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.ASCII.GetBytes(input + "\u001b[?47;1047;1049;7;25;1007$p");
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen expected = new(12, 4), actual = new(12, 4);
            using GhosttyVtProcessor native = new(expected);
            using BasicVtProcessor managed = new(actual);
            List<byte> expectedReplies = [], actualReplies = [];
            native.ResponseCallback = data => expectedReplies.AddRange(data);
            managed.ResponseCallback = data => actualReplies.AddRange(data);
            native.Process(bytes.AsSpan(0, split)); native.Process(bytes.AsSpan(split));
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(expectedReplies, actualReplies);
            Assert.Equal(native.AlternateScreen, managed.AlternateScreen);
            Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        }
    }

    [Theory]
    [InlineData("\u001b[2;4r\u001b[?6h\u001b[?6s\u001b[3;5H\u001b[?6rX")]
    [InlineData("\u001b[?69h\u001b[3;10s\u001b[?69l\u001b[?69s\u001b[?69h\u001b[3;10s\u001b[?69r\u001b[Habcdefghijklm")]
    [InlineData("\u001b[2;5H\u001b[?1048h\u001b[?1048s\u001b[3;7H\u001b[?1048r\u001b[H\u001b[?1048lX")]
    [InlineData("primary\u001b[?1049h\u001b[?1049sALT\u001b[?1049rNEW\u001b[?1049lX")]
    public void RestoreExecutesCursorMarginAndScreenSideEffects(string input)
    {
        if (!Available()) return;
        TerminalScreen expected = new(12, 4), actual = new(12, 4);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        byte[] bytes = Encoding.ASCII.GetBytes(input);
        native.Process(bytes); managed.Process(bytes);
        Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        Assert.Equal(native.AlternateScreen, managed.AlternateScreen);
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 12; column++)
            Assert.Equal(expected.GetViewportRow(row)[column].Codepoint, actual.GetViewportRow(row)[column].Codepoint);
    }

    [Fact]
    public void RestoringSavedSynchronizedOutputPublishesOnlyOnRelease()
    {
        TerminalScreen screen = new(12, 4);
        using BasicVtProcessor managed = new(screen);
        managed.Process("A\u001b[?2026h\u001b[?2026sB\u001b[?2026l\u001b[?2026rC"u8);
        Assert.Equal('B', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal(0, screen.GetViewportRow(0)[2].Codepoint);
        managed.Process("\u001b[?2026rD"u8); // Same-state restore must not publish half a frame.
        Assert.Equal(0, screen.GetViewportRow(0)[2].Codepoint);
        managed.Process("\u001b[?2026l"u8);
        Assert.Equal('C', screen.GetViewportRow(0)[2].Codepoint);
        Assert.Equal('D', screen.GetViewportRow(0)[3].Codepoint);
    }

    [Fact]
    public void SaveRestoreAndBankReadsAllocateNothingAfterWarmup()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(12, 4));
        ReadOnlySpan<byte> sequence = "\u001b[?7;25;1007s\u001b[?7;25;1007l\u001b[?7;25;1007r"u8;
        for (int i = 0; i < 100; i++) { managed.Process(sequence); _ = managed.SnapshotCurrentModes; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { managed.Process(sequence); _ = managed.SnapshotCurrentModes; }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(BasicVtProcessor.SnapshotInitialModes, managed.SnapshotSavedModes);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native mode differential available: {available}");
        return available;
    }

    private static GhosttySnapshotTerminalHeader Header(GhosttyTerminal terminal)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(terminal), new());
        return reader.ReadReady().Terminal.Header;
    }
}
