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

public sealed class TerminalCursorParityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Policies()
    {
        foreach (TerminalCursorStyle style in new[] { TerminalCursorStyle.Block, TerminalCursorStyle.Bar, TerminalCursorStyle.Underline, TerminalCursorStyle.BlockHollow })
        foreach (bool blink in new[] { false, true })
        for (int explicitStyle = 0; explicitStyle <= 6; explicitStyle++) yield return [style, blink, explicitStyle];
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void SharedPoliciesRespectExplicitAppearanceQueriesAndSessionResets(TerminalCursorStyle style, bool blink, int explicitStyle)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        List<byte> expected = [], actual = [];
        native.ResponseCallback = bytes => expected.AddRange(bytes);
        managed.ResponseCallback = bytes => actual.AddRange(bytes);
        void Compare()
        {
            Assert.Equal(native.CursorStyle, managed.CursorStyle);
            Assert.Equal(native.CursorBlinking, managed.CursorBlinking);
            native.Process("\u001bP$q q\u001b\\\u001b[?12$p"u8);
            managed.Process("\u001bP$q q\u001b\\\u001b[?12$p"u8);
            Assert.Equal(expected, actual);
        }
        void Process(string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            native.Process(bytes); managed.Process(bytes); Compare();
        }
        Compare();
        Process($"\u001b[{explicitStyle} q");
        ((ITerminalCursorDefaults)native).SetDefaultCursorStyle(style);
        ((ITerminalCursorDefaults)managed).SetDefaultCursorStyle(style);
        Compare();
        native.SetDefaultCursorBlink(blink); managed.SetDefaultCursorBlink(blink); Compare();
        Process("\u001b[?12h"); Process("\u001b[?12s\u001b[?12l"); Process("\u001b[?12r");
        // A raw mode change doesn't stop the cursor following defaults.
        native.SetDefaultCursorStyle(style); managed.SetDefaultCursorStyle(style); Compare();
        Process("\u001b[0 q");
        Assert.Equal(style, managed.CursorStyle); Assert.Equal(blink, managed.CursorBlinking);
        Process("\u001b[1 q");
        native.Reset(); managed.Reset(); Compare();
        Assert.Equal(style, managed.CursorStyle); Assert.Equal(blink, managed.CursorBlinking);
        Process("history\r\n\u001b[5 q");
        native.PrepareForNewSession(preserveScrollback: true);
        managed.PrepareForNewSession(preserveScrollback: true); Compare();
        Assert.Equal(style, managed.CursorStyle); Assert.Equal(blink, managed.CursorBlinking);
    }

    [Theory]
    [InlineData("\u001b[ q")]
    [InlineData("\u001b[0 q")]
    [InlineData("\u001b[7 q")]
    [InlineData("\u001b[65535 q")]
    [InlineData("\u001b[1;2 q")]
    [InlineData("\u001b[; q")]
    [InlineData("\u001b[1:2 q")]
    [InlineData("\u001b[?2 q")]
    [InlineData("\u001b[2  q")]
    [InlineData("\u001b[6 q\u001b[?12h")]
    public void CursorCommandsMatchNativeAtEverySplit(string command)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.ASCII.GetBytes(command);
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyTerminal native = new(8, 3);
            using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
            native.Write("\u001b[3 q"u8); managed.Process("\u001b[3 q"u8);
            native.Write(bytes.AsSpan(0, split)); native.Write(bytes.AsSpan(split));
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            CompareSnapshot(native, managed);
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(1047)]
    [InlineData(1049)]
    public void ShapesBelongToEachScreenButBlinkAndDefaultSelectionAreGlobal(int mode)
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 3);
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        string[] commands = ["\u001b[3 q", $"\u001b[?{mode}h", "\u001b[6 q", $"\u001b[?{mode}l",
            "\u001b7\u001b[1 q\u001b8", "\u001b[?1049h", "\u001b[4 q", "\u001b[?1049h",
            "\u001b[?1049l", "\u001b[?47h", "\u001b[0 q", "\u001b[?47l", "\u001bc"];
        foreach (string command in commands)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes); managed.Process(bytes); CompareSnapshot(native, managed);
        }
    }

    public static IEnumerable<object[]> SnapshotPolicies()
    {
        for (byte style = 0; style < 4; style++)
        for (byte blink = 0; blink < 3; blink++)
        for (byte follows = 0; follows < 2; follows++) yield return [style, blink, follows];
    }

    [Theory]
    [MemberData(nameof(SnapshotPolicies))]
    public void SnapshotInstallationPreservesPolicyAndIndependentScreenShapes(byte style, byte blink, byte follows)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[0].Payload[21] = records[0].Payload[22] = 0;
        records[0].Payload[29] = follows; records[0].Payload[30] = style; records[0].Payload[31] = blink;
        for (int i = 0; i < records.Count; i++)
        {
            SnapshotTestRecord record = records[i];
            if (record.Tag == GhosttySnapshotRecordTag.Screen) record.Payload[16] = (byte)((style + record.Payload[0]) % 4);
            if (record.Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(record.Tag, []);
        }
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        GhosttySnapshotReadyState ready = Read(native);
        using BasicVtProcessor managed = new(new TerminalScreen(ready.Terminal.Header.Columns, ready.Terminal.Header.Rows));
        managed.InstallSnapshotModes(ready.Terminal.Header);
        managed.InstallSnapshotCursorPolicy(ready.Terminal.Header);
        foreach (GhosttySnapshotScreen screen in ready.Screens) managed.InstallSnapshotCursorStyle(screen.State);
        CompareSnapshot(native, managed);
        native.SetDefaultCursorStyle(GhosttyVtNative.GhosttyTerminalCursorStyle.BlockHollow);
        managed.SetDefaultCursorStyle(TerminalCursorStyle.BlockHollow); CompareSnapshot(native, managed);
        foreach (string command in new[] { "\u001b[0 q", "\u001b[?1049h", "\u001b[5 q", "\u001b[?1049l", "\u001bc" })
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes); managed.Process(bytes); CompareSnapshot(native, managed);
        }
    }

    [Fact]
    public void HeldCursorPresentationStaysFrozenWhileLiveQueriesAndPolicyAdvance()
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        native.Process("\u001b[?2026h"u8); managed.Process("\u001b[?2026h"u8);
        native.SetDefaultCursorStyle(TerminalCursorStyle.BlockHollow); managed.SetDefaultCursorStyle(TerminalCursorStyle.BlockHollow);
        native.SetDefaultCursorBlink(true); managed.SetDefaultCursorBlink(true);
        Assert.Equal(TerminalCursorStyle.Block, managed.CursorStyle); Assert.False(managed.CursorBlinking);
        Assert.Equal(native.CursorStyle, managed.CursorStyle); Assert.Equal(native.CursorBlinking, managed.CursorBlinking);
        byte[]? expected = null, actual = null;
        native.ResponseCallback = bytes => expected = bytes; managed.ResponseCallback = bytes => actual = bytes;
        native.Process("\u001bP$q q\u001b\\"u8); managed.Process("\u001bP$q q\u001b\\"u8);
        Assert.Equal(expected, actual); Assert.Equal("\u001bP1$r1 q\u001b\\"u8.ToArray(), actual);
        native.Process("\u001b[?2026l"u8); managed.Process("\u001b[?2026l"u8);
        Assert.Equal(TerminalCursorStyle.BlockHollow, managed.CursorStyle); Assert.True(managed.CursorBlinking);
        Assert.Equal(native.CursorStyle, managed.CursorStyle); Assert.Equal(native.CursorBlinking, managed.CursorBlinking);
    }

    [Fact]
    public void PolicyAndShapeReadsAllocateNothingAndRejectInvalidStyles()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        for (int i = 0; i < 1000; i++) { managed.SetDefaultCursorStyle(TerminalCursorStyle.Bar); managed.SetDefaultCursorBlink(true); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            managed.SetDefaultCursorStyle(TerminalCursorStyle.Bar); managed.SetDefaultCursorBlink(true);
            _ = managed.CursorStyle; _ = managed.CursorBlinking; _ = managed.SnapshotCursorPolicy;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Throws<ArgumentOutOfRangeException>(() => managed.SetDefaultCursorStyle((TerminalCursorStyle)255));
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => native.SetDefaultCursorStyle((TerminalCursorStyle)255));
        native.Dispose();
        Assert.Throws<ObjectDisposedException>(() => native.SetDefaultCursorStyle(TerminalCursorStyle.Bar));
        Assert.Throws<ObjectDisposedException>(() => native.SetDefaultCursorBlink(true));
    }

    private static GhosttySnapshotReadyState Read(GhosttyTerminal terminal)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(terminal), new());
        return reader.ReadReady();
    }

    private static void CompareSnapshot(GhosttyTerminal native, BasicVtProcessor managed)
    {
        GhosttySnapshotReadyState ready = Read(native);
        GhosttySnapshotTerminalHeader header = ready.Terminal.Header;
        Assert.Equal((Map(header.CursorDefaultStyle), header.CursorDefaultBlink, header.CursorIsDefault), managed.SnapshotCursorPolicy);
        Assert.Equal((header.CurrentModes & (1UL << 12)) != 0, managed.CursorBlinking);
        foreach (GhosttySnapshotScreen screen in ready.Screens)
            Assert.Equal(Map(screen.State.CursorStyle), managed.GetSnapshotCursorStyle(screen.State.Key));
        Assert.Equal(managed.GetSnapshotCursorStyle(header.ActiveScreenKey), managed.CursorStyle);
    }

    private static TerminalCursorStyle Map(byte value) => value switch
    {
        0 => TerminalCursorStyle.Bar, 1 => TerminalCursorStyle.Block,
        2 => TerminalCursorStyle.Underline, 3 => TerminalCursorStyle.BlockHollow,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native cursor differential available: {available}");
        return available;
    }
}
