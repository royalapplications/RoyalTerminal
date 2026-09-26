// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSnapshotGraphemeLimitTests
{
    [Theory]
    [InlineData(false, 63)]
    [InlineData(false, 64)]
    [InlineData(false, 65)]
    [InlineData(false, 200)]
    [InlineData(true, 64)]
    [InlineData(true, 65)]
    [InlineData(true, 200)]
    public void RestoredLiveCellsUseInputBoundWithoutChangingTheWireCodec(bool supplementary, int count)
    {
        string suffix = supplementary ? "\U000E0100" : "\u0301";
        byte[] bytes = Snapshot(suffix, count);
        using GhosttySnapshotStateReader reader = new(bytes, new());
        Assert.Equal(count, Assert.Single(reader.ReadReady().Screens[0].Pages).Grid.Suffix(0, 0).Length);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes);
        string expected = "A" + string.Concat(Enumerable.Repeat(suffix, Math.Min(count, 64)));
        Assert.Equal(expected, restored.Screen.GetViewportRow(0).ReadOnlyCells[0].Grapheme);
        Assert.Equal(1, restored.Processor.CursorCol);
        restored.Processor.Process(Encoding.UTF8.GetBytes(suffix));
        if (count >= 64)
            Assert.Equal(expected, restored.Screen.GetViewportRow(0).ReadOnlyCells[0].Grapheme);
        restored.Processor.Process("B"u8);
        Assert.Equal('B', restored.Screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
        Assert.Equal(2, restored.Processor.CursorCol);
    }

    [Fact]
    public void DroppedSuffixStillCountsAgainstTheHardDecodeLimit()
    {
        byte[] bytes = Snapshot("\u0301", 200);
        using ManagedTerminalSnapshotDecoder decoder = new(bytes, new()
        {
            DecodeLimits = new(MaximumSuffixCodepointsPerPage: 64),
        });
        Assert.Throws<InvalidDataException>(() => decoder.Ready());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreAndSubsequentInputMatchNativeSuffixStorage(bool supplementary)
    {
        RequireNative();
        string suffix = supplementary ? "\U000E0100" : "\u0301";
        byte[] bytes = Snapshot(suffix, 200);
        using GhosttyTerminal native = GhosttySnapshot.Decode(bytes);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(bytes);
        AssertSameCells(native, managed);
        foreach (byte[] input in new[] { Encoding.UTF8.GetBytes(suffix), "B"u8.ToArray(), "\u001b[2b"u8.ToArray() })
        {
            native.Write(input);
            managed.Processor.Process(input);
            AssertSameCells(native, managed);
        }
    }

    private static byte[] Snapshot(string suffix, int count)
    {
        TerminalScreen screen = new(4, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process("A"u8);
        screen.GetViewportRow(0)[0].Grapheme = "A" + string.Concat(Enumerable.Repeat(suffix, count));
        return processor.GetBinarySnapshot();
    }

    private static void AssertSameCells(GhosttyTerminal native, ManagedTerminalSnapshot managed)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotGrid grid = Assert.Single(reader.ReadReady().Screens[0].Pages).Grid;
        TerminalRow row = managed.Screen.GetViewportRow(0);
        Assert.Equal(64, grid.Suffix(0, 0).Length);
        StringBuilder text = new("A");
        foreach (uint scalar in grid.Suffix(0, 0)) text.Append(char.ConvertFromUtf32((int)scalar));
        Assert.Equal(text.ToString(), row.ReadOnlyCells[0].Grapheme);
        for (int column = 0; column < 4; column++)
            Assert.Equal((int)((grid.Cells[column] >> 2) & 0xFFFFFF), row.ReadOnlyCells[column].Codepoint);
        Assert.Equal((int)native.GetCursorX(), managed.Processor.CursorCol);
        Assert.Equal((int)native.GetCursorY(), managed.Processor.CursorRow);
        using GhosttySnapshotStateReader managedReader = new(managed.Processor.GetBinarySnapshot(), new());
        Assert.Equal(native.GetCursorPendingWrap(), managedReader.ReadReady().Screens[0].State.PendingWrap);
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
