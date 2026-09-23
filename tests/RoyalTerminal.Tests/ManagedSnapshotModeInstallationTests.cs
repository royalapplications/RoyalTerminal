// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSnapshotModeInstallationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Banks()
    {
        yield return [0UL]; yield return [GhosttySnapshotTerminalHeader.ModeMask];
        for (int i = 0; i < 43; i++) yield return [1UL << i];
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void AllBanksInstallWithoutExecutingModeSideEffects(ulong values)
    {
        GhosttySnapshotTerminalHeader header = Header(values, values ^ GhosttySnapshotTerminalHeader.ModeMask, values);
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor managed = new(screen);
        managed.Process("AB"u8);
        int replies = 0, changes = 0;
        managed.ResponseCallback = _ => replies++;
        managed.ModeChanged += (_, _) => changes++;
        managed.InstallSnapshotModes(header);
        Assert.Equal(values, managed.SnapshotCurrentModes);
        Assert.Equal(header.SavedModes, managed.SnapshotSavedModes);
        Assert.Equal(values, managed.SnapshotDefaultModes);
        Assert.Equal((8, 3, 2, 0, false), (screen.Columns, screen.ViewportRows, managed.CursorCol, managed.CursorRow, managed.AlternateScreen));
        Assert.Equal('A', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('B', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal((0, 0), (replies, changes));
        // Raw synchronized-output bits must not create/publish a half-built hold.
        managed.Process("X"u8);
        Assert.Equal('X', screen.GetViewportRow(0)[2].Codepoint);
    }

    [Theory]
    [MemberData(nameof(Banks))]
    public void ArbitraryRestoredDefaultsAndSavedBanksMatchNativeAfterRis(ulong defaults)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = Records(BasicVtProcessor.SnapshotInitialModes, defaults, defaults);
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        GhosttySnapshotTerminalHeader initial = NativeHeader(native);
        using BasicVtProcessor managed = new(new TerminalScreen(initial.Columns, initial.Rows));
        managed.InstallSnapshotModes(initial);
        AssertBanks(native, managed);
        native.Write("\u001bc"u8); managed.Process("\u001bc"u8);
        AssertBanks(native, managed);
        Assert.Equal(default(TerminalMouseModeState), managed.SnapshotMouseModes);
        Assert.Equal(defaults, managed.SnapshotDefaultModes);
    }

    [Fact]
    public void RestoredMouseFlagsAreIndependentOfModeBitsAndFollowLaterCommands()
    {
        if (!Available()) return;
        for (byte tracking = 0; tracking < 5; tracking++)
        for (byte format = 0; format < 5; format++)
        {
            List<SnapshotTestRecord> records = Records(0, 0, BasicVtProcessor.SnapshotInitialModes);
            records[0].Payload[34] = tracking; records[0].Payload[35] = format;
            using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
            using BasicVtProcessor managed = new(new TerminalScreen(2, 3));
            managed.InstallSnapshotModes(NativeHeader(native));
            AssertBanks(native, managed);
            foreach (string command in new[] { "\u001b[?1006h", "\u001b[?9l", "\u001b[?1003h", "\u001b[?1016l", "\u001b[?1003s\u001b[?9h\u001b[?1003r" })
            {
                byte[] bytes = Encoding.ASCII.GetBytes(command);
                native.Write(bytes); managed.Process(bytes);
                AssertBanks(native, managed);
            }
        }
    }

    [Fact]
    public void WarmModeInstallationAndReadsAllocateNothing()
    {
        GhosttySnapshotTerminalHeader header = Header(GhosttySnapshotTerminalHeader.ModeMask, 0, 0);
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        for (int i = 0; i < 1000; i++) { managed.InstallSnapshotModes(header); _ = managed.SnapshotCurrentModes; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { managed.InstallSnapshotModes(header); _ = managed.SnapshotCurrentModes; }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static GhosttySnapshotTerminalHeader Header(ulong current, ulong saved, ulong defaults)
        => GhosttySnapshotTerminalHeader.Read(Records(current, saved, defaults)[0].Payload, 100);

    private static List<SnapshotTestRecord> Records(ulong current, ulong saved, ulong defaults)
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        byte[] terminal = records[0].Payload;
        BinaryPrimitives.WriteUInt64LittleEndian(terminal.AsSpan(39), current);
        BinaryPrimitives.WriteUInt64LittleEndian(terminal.AsSpan(47), saved);
        BinaryPrimitives.WriteUInt64LittleEndian(terminal.AsSpan(55), defaults);
        terminal[31] = 0; // No cursor blink policy overriding the restored bank at RIS.
        terminal[34] = terminal[35] = 0;
        for (int i = 0; i < records.Count; i++)
            if (records[i].Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(records[i].Tag, []);
        return records;
    }

    private static void AssertBanks(GhosttyTerminal native, BasicVtProcessor managed)
    {
        GhosttySnapshotTerminalHeader expected = NativeHeader(native);
        Assert.Equal(expected.CurrentModes, managed.SnapshotCurrentModes);
        Assert.Equal(expected.SavedModes, managed.SnapshotSavedModes);
        Assert.Equal(expected.DefaultModes, managed.SnapshotDefaultModes);
        Assert.Equal(new TerminalMouseModeState((TerminalMouseTrackingMode)expected.MouseEvent, (TerminalMouseEncoding)expected.MouseFormat), managed.SnapshotMouseModes);
    }

    internal static GhosttySnapshotTerminalHeader NativeHeader(GhosttyTerminal native)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        return reader.ReadReady().Terminal.Header;
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot mode differential available: {available}");
        return available;
    }
}
