// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #14316 uses fixed mode lookup and packed live state. WT's dispatch
// mode enumset and xterm.js's explicit mode fields also avoid per-cell hash
// lookups. This container changes representation only, not semantic dispatch
// or Ghostty snapshot-v1's independent mode-bank ordering.
public sealed class ManagedDecModeStateTests
{
    public static IEnumerable<object[]> Modes()
    {
        int[] modes = [3, 4, 5, 8, 9, 12, 40, 45, 69, 1000, 1002, 1003, 1004, 1005,
            1006, 1007, 1015, 1016, 1035, 1036, 1039, 1045, 2026, 2027, 2031, 2033, 2048, 5522];
        foreach (int mode in modes) yield return [mode];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void EverySupportedModeHasAnIndependentBitAndRetainsProtocolQueries(int mode)
    {
        ManagedDecModeState state = default;
        Assert.True(state.TryGet(mode, out bool enabled));
        Assert.False(enabled);
        state.Set(mode, true);
        foreach (int other in ManagedDecModeState.SupportedModes)
        {
            Assert.True(state.TryGet(other, out enabled));
            Assert.Equal(other == mode, enabled);
        }
        ManagedDecModeState copy = state;
        state.Set(mode, false);
        Assert.False(state.Contains(mode));
        Assert.True(copy.Contains(mode));

        using BasicVtProcessor processor = new(new TerminalScreen(16, 3));
        processor.Process("\u001b[?40h"u8); // DECCOLM remains conditional on allow-column mode.
        string? reply = null;
        processor.ResponseCallback = bytes => reply = Encoding.ASCII.GetString(bytes);
        foreach (bool set in new[] { true, false, true, false })
        {
            processor.Process(Encoding.ASCII.GetBytes($"\u001b[?{mode}{(set ? 'h' : 'l')}\u001b[?{mode}$p"));
            Assert.Equal($"\u001b[?{mode};{(set ? 1 : 2)}$y", reply);
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(47)]
    [InlineData(2004)]
    [InlineData(5523)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void UnknownAndSeparatelyStoredModesNeverAliasAnExtendedFlag(int mode)
    {
        ManagedDecModeState state = default;
        state.Reset();
        state.Set(mode, true);
        Assert.False(state.TryGet(mode, out bool enabled));
        Assert.False(enabled);
        Assert.False(state.Contains(mode));
        state.Set(mode, false);
        foreach (int known in ManagedDecModeState.SupportedModes)
            Assert.Equal(known is 1007 or 1035 or 1036, state.Contains(known));
    }

    [Fact]
    public void FixedLookupExactlyMatchesItsRegistryAcrossTheWireRange()
    {
        uint bits = 0;
        foreach (int mode in ManagedDecModeState.SupportedModes)
        {
            uint bit = (uint)ManagedDecModeState.FlagFor(mode);
            Assert.NotEqual(0U, bit);
            Assert.Equal(0U, bit & (bit - 1));
            Assert.Equal(0U, bits & bit);
            bits |= bit;
        }
        Assert.Equal((1U << 28) - 1, bits);
        for (int mode = 0; mode <= ushort.MaxValue; mode++)
            Assert.Equal(ManagedDecModeState.SupportedModes.Contains(mode),
                ManagedDecModeState.FlagFor(mode) != ManagedDecModeFlag.None);
        Assert.Equal(ManagedDecModeFlag.GraphemeClusters, ManagedDecModeState.FlagFor(2027));
        Assert.Equal(ManagedDecModeFlag.CursorBlink, ManagedDecModeState.FlagFor(12));
        Assert.Equal(ManagedDecModeFlag.FocusEvents, ManagedDecModeState.FlagFor(1004));
        Assert.Equal(ManagedDecModeFlag.LeftRightMargins, ManagedDecModeState.FlagFor(69));
        Assert.Equal(ManagedDecModeFlag.AllowColumnMode, ManagedDecModeState.FlagFor(40));
        Assert.Equal(ManagedDecModeFlag.KittyClipboard, ManagedDecModeState.FlagFor(5522));
        Assert.Equal(ManagedDecModeFlag.VisibilityReports, ManagedDecModeState.FlagFor(2033));
        Assert.Equal(ManagedDecModeFlag.SizeReports, ManagedDecModeState.FlagFor(2048));
        Assert.Equal(ManagedDecModeFlag.ColorSchemeReports, ManagedDecModeState.FlagFor(2031));
        Assert.Equal(ManagedDecModeFlag.NumLockKeypad, ManagedDecModeState.FlagFor(1035));
        Assert.Equal(ManagedDecModeFlag.AltEscapePrefix, ManagedDecModeState.FlagFor(1036));
    }

    [Fact]
    public void ResetDiscardsAllBitsBeforeRestoringBuiltInDefaults()
    {
        ManagedDecModeState state = default;
        foreach (int mode in ManagedDecModeState.SupportedModes) state.Set(mode, true);
        state.Reset();
        foreach (int mode in ManagedDecModeState.SupportedModes)
            Assert.Equal(mode is 1007 or 1035 or 1036, state.Contains(mode));
        state.Set(1007, false);
        state.Reset();
        Assert.True(state.Contains(ManagedDecModeFlag.AlternateScroll));
    }

    [Fact]
    public void CreationTogglingReadsAndResetDoNotAllocate()
    {
        _ = Exercise();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int enabled = Exercise();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000, enabled);
        Assert.Equal(0, allocated);

        static int Exercise()
        {
            int enabled = 0;
            for (int i = 0; i < 1000; i++)
            {
                ManagedDecModeState state = default;
                state.Reset();
                foreach (int mode in ManagedDecModeState.SupportedModes)
                {
                    state.Set(mode, true);
                    state.Set(mode, false);
                }
                state.Set(2027, true);
                if (state.Contains(ManagedDecModeFlag.GraphemeClusters)) enabled++;
                state.Reset();
            }
            return enabled;
        }
    }

    [Fact]
    public void FirstSnapshotModeInstallationDoesNotGrowAContainerOrChangeWireOrdering()
    {
        byte[] payload = SnapshotTestRecords.Fixture()[0].Payload;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(39), GhosttySnapshotTerminalHeader.ModeMask);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(47), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(55), BasicVtProcessor.SnapshotInitialModes);
        GhosttySnapshotTerminalHeader header = GhosttySnapshotTerminalHeader.Read(payload, 100);
        // Warm code separately, without growing or priming the measured owner.
        using (BasicVtProcessor warm = new(new TerminalScreen(8, 3)))
        {
            warm.InstallSnapshotModes(header);
            _ = warm.SnapshotCurrentModes;
        }
        using BasicVtProcessor cold = new(new TerminalScreen(8, 3));
        long before = GC.GetAllocatedBytesForCurrentThread();
        cold.InstallSnapshotModes(header);
        ulong values = cold.SnapshotCurrentModes;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(GhosttySnapshotTerminalHeader.ModeMask, values);
        Assert.Equal(0UL, cold.SnapshotSavedModes);
        Assert.Equal(BasicVtProcessor.SnapshotInitialModes, cold.SnapshotDefaultModes);
        Assert.Equal(0, allocated);
    }
}
