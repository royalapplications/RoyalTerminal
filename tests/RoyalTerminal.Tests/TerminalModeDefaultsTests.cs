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

public sealed class TerminalModeDefaultsTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> ConfigurableModes()
    {
        foreach (int mode in new[] { 2, 4, 12, 20 }) yield return [mode, true];
        foreach (int mode in new[] { 1, 4, 5, 7, 8, 25, 40, 45, 66, 67, 1004, 1007,
            1035, 1036, 1039, 1045, 2004, 2027, 2031, 2048, 5522 }) yield return [mode, false];
    }

    public static IEnumerable<object[]> RejectedModes()
    {
        foreach (int mode in new[] { 3, 6, 9, 12, 47, 69, 1000, 1002, 1003, 1005,
            1006, 1015, 1016, 1047, 1048, 1049, 2026, 2033, 80, 117, 9001, 9999 }) yield return [mode, false];
        yield return [1, true]; yield return [7, true];
        yield return [-1, false]; yield return [65537, false];
    }

    [Theory]
    [MemberData(nameof(ConfigurableModes))]
    public void CurrentSavedAndDefaultBanksMatchNativeAcrossWritesAndRis(int mode, bool ansi)
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(12, 4);
        using BasicVtProcessor managed = new(new TerminalScreen(12, 4));
        foreach (bool enabled in new[] { true, false, true })
        {
            native.SetDefaultMode(GhosttyVtNative.CreateMode((ushort)mode, ansi), enabled);
            Assert.True(managed.TrySetDefaultMode(mode, enabled, ansi));
            Compare();
            Write($"\u001b[{(ansi ? "" : "?")}{mode}{(enabled ? 'l' : 'h')}");
            Compare();
            // Reapplying an unchanged default must overwrite a changed current value.
            native.SetDefaultMode(GhosttyVtNative.CreateMode((ushort)mode, ansi), enabled);
            Assert.True(managed.TrySetDefaultMode(mode, enabled, ansi));
            Compare();
            if (!ansi) Write($"\u001b[?{mode}s");
            Write("\u001bc");
            Compare();
            if (!ansi) { Write($"\u001b[?{mode}r"); Compare(); }
        }
        void Write(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            native.Write(bytes); managed.Process(bytes);
        }
        void Compare()
        {
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotTerminalHeader header = reader.ReadReady().Terminal.Header;
            Assert.Equal(header.CurrentModes, managed.SnapshotCurrentModes);
            Assert.Equal(header.SavedModes, managed.SnapshotSavedModes);
            Assert.Equal(header.DefaultModes, managed.SnapshotDefaultModes);
        }
    }

    [Theory]
    [MemberData(nameof(RejectedModes))]
    public void InvalidPolicyDoesNotMutateEitherProcessor(int mode, bool ansi)
    {
        using BasicVtProcessor managed = new(new TerminalScreen(12, 4));
        ulong current = managed.SnapshotCurrentModes, defaults = managed.SnapshotDefaultModes;
        Assert.False(managed.TrySetDefaultMode(mode, true, ansi));
        Assert.Equal(current, managed.SnapshotCurrentModes);
        Assert.Equal(defaults, managed.SnapshotDefaultModes);
        Assert.Equal(TerminalModeRegistry.InitialValues, managed.SnapshotSavedModes);
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(12, 4));
        TerminalModeState before = native.ModeState;
        Assert.False(native.TrySetDefaultMode(mode, true, ansi));
        Assert.Equal(before, native.ModeState);
        // Native's 15-bit tag cannot represent the API range-check cases.
        if (mode is >= 0 and <= 32767)
        {
            using GhosttyTerminal raw = new(12, 4);
            byte[] initial = GhosttySnapshot.Encode(raw);
            Assert.Throws<InvalidOperationException>(() => raw.SetDefaultMode(GhosttyVtNative.CreateMode((ushort)mode, ansi), true));
            Assert.Equal(initial, GhosttySnapshot.Encode(raw));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PolicySurvivesSessionPreparationAndSavedBankResetsIndependently(bool native, bool preserveHistory)
    {
        if (native && !Available()) return;
        TerminalScreen screen = new(12, 4);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        ITerminalModeDefaults policy = Assert.IsAssignableFrom<ITerminalModeDefaults>(processor);
        Assert.True(policy.TrySetDefaultMode(1, true));
        Assert.True(policy.TrySetDefaultMode(7, false));
        Assert.True(policy.TrySetDefaultMode(2004, true));
        Assert.True(policy.TrySetDefaultMode(4, true, ansi: true));
        processor.Process("OLD\u001b[?1;7;2004s\u001b[?1;2004l\u001b[?7h\u001b[4l"u8);
        ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserveHistory);
        Assert.True(processor.ApplicationCursorKeys);
        Assert.True(processor.BracketedPaste);
        List<string> replies = [];
        processor.ResponseCallback = data => replies.Add(Encoding.ASCII.GetString(data));
        processor.Process("\u001b[?7$p\u001b[4$p\u001b[?1;7;2004r\u001b[?1$p\u001b[?7$p\u001b[?2004$p"u8);
        Assert.Equal(new[] { "\u001b[?7;2$y", "\u001b[4;1$y", "\u001b[?1;2$y", "\u001b[?7;1$y", "\u001b[?2004;2$y" }, replies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultWritesDoNotReplayTransitionEffectsOrLosePendingWrap(bool native)
    {
        if (native && !Available()) return;
        TerminalScreen screen = new(4, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        ITerminalModeDefaults policy = (ITerminalModeDefaults)processor;
        int replies = 0;
        processor.ResponseCallback = _ => replies++;
        processor.Process("ABCD"u8);
        Assert.True(policy.TrySetDefaultMode(7, false));
        Assert.True(policy.TrySetDefaultMode(7, true));
        Assert.True(policy.TrySetDefaultMode(2048, true));
        Assert.Equal(0, replies);
        processor.Process("X"u8);
        Assert.Equal((1, 1), (processor.CursorCol, processor.CursorRow));
        Assert.Equal('D', screen.GetViewportRow(0)[3].Codepoint);
        Assert.Equal('X', screen.GetViewportRow(1)[0].Codepoint);
    }

    [Fact]
    public void ManagedPolicyAndResetReadsDoNotAllocateAfterWarmup()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(12, 4));
        for (int i = 0; i < 100; i++) managed.TrySetDefaultMode(2027, (i & 1) != 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            managed.TrySetDefaultMode(2027, (i & 1) != 0);
            _ = managed.SnapshotDefaultModes;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native mode-default differential available: {available}");
        return available;
    }
}
