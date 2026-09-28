// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// PageList.increaseCapacity, RefCountedSet.Layout and BitmapAllocator define
// these logical native units. CLR object sizes are deliberately not an oracle.
public sealed class GhosttySnapshotMetadataGrowthTests
{
    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void ZeroDimensionsResumeAtNativeDefaults(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity empty = new(80, 4, 0, 0, 0, 0);
        foreach (GhosttySnapshotCapacityDimension dimension in new[]
        {
            GhosttySnapshotCapacityDimension.Styles, GhosttySnapshotCapacityDimension.GraphemeBytes,
            GhosttySnapshotCapacityDimension.HyperlinkBytes, GhosttySnapshotCapacityDimension.StringBytes,
        })
        {
            Assert.True(layout.TryIncreaseCapacity(empty, dimension, 0, 4, out GhosttySnapshotPageCapacity grown));
            Assert.Equal(dimension switch
            {
                GhosttySnapshotCapacityDimension.Styles => empty with { Styles = 16 },
                GhosttySnapshotCapacityDimension.GraphemeBytes => empty with { GraphemeBytes = 1024 },
                GhosttySnapshotCapacityDimension.HyperlinkBytes => empty with { HyperlinkBytes = 192 },
                _ => empty with { StringBytes = 2048 },
            }, grown);
        }
    }

    [Fact]
    public void GrowthProjectsDensityWithHeadroomAndThirtyTwoTimesBound()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = new(80, 100, 128, 192, 8192, 2048);
        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles, 100, 10, out var projected));
        Assert.Equal((ushort)1250, projected.Styles);
        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles, 100, 0, out var noRows));
        Assert.Equal((ushort)256, noRows.Styles);
        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles, 0, 1, out var unused));
        Assert.Equal((ushort)256, unused.Styles);
        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles, ulong.MaxValue, 1, out var bounded));
        Assert.Equal((ushort)4096, bounded.Styles);
        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.GraphemeBytes, 8192, 1, out var graphemes));
        Assert.Equal(8192U * 32, graphemes.GraphemeBytes);
        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.StringBytes, 2048, 1, out var strings));
        Assert.Equal(4096U, strings.StringBytes); // No projection for strings/links.
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void CeilingRejectsGrowthOrKeepsDoublingWhenOnlyProjectionIsTooLarge(int alignment)
    {
        GhosttySnapshotAllocation layout = new(alignment);
        GhosttySnapshotPageCapacity large = new(65535, 8000, 0, 0, 32 * 1024 * 1024, 0);
        Assert.True(layout.LayoutBytes(large) <= uint.MaxValue);
        Assert.False(layout.TryIncreaseCapacity(large, GhosttySnapshotCapacityDimension.GraphemeBytes, 0, 1, out var rejected));
        Assert.Equal(large, rejected);
        GhosttySnapshotPageCapacity project = large with { GraphemeBytes = 2 * 1024 * 1024 };
        Assert.True(layout.TryIncreaseCapacity(project, GhosttySnapshotCapacityDimension.GraphemeBytes, project.GraphemeBytes, 1, out var doubled));
        Assert.Equal(4U * 1024 * 1024, doubled.GraphemeBytes);
        GhosttySnapshotPageCapacity almost = new(1, 1, 40000, 40000, 0, 0);
        Assert.True(layout.TryIncreaseCapacity(almost, GhosttySnapshotCapacityDimension.Styles, 0, 1, out var maximum));
        Assert.Equal(ushort.MaxValue, maximum.Styles);
        Assert.False(layout.TryIncreaseCapacity(maximum, GhosttySnapshotCapacityDimension.Styles, 0, 1, out _));
    }

    [Fact]
    public void MetadataFitsUseUsableSlotsAndBitmapChunksInsteadOfRawHints()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = new(80, 4, 128, 192, 1, 1);
        // 128 style buckets hold 103 values after the reserved zero ID; a
        // 192-byte hyperlink request holds two links and 102 linked cells.
        // Nonzero bitmap requests round to 64 chunks (1024/2048 data bytes).
        GhosttySnapshotMetadataUsage full = new(103, 1, 16, 0, 2, 102, 2048);
        Assert.True(layout.TryFitMetadata(capacity, full, 4, out var unchanged));
        Assert.Equal(capacity, unchanged);
        Assert.True(layout.TryFitMetadata(capacity, full with { Styles = 104 }, 4, out var styles));
        Assert.Equal((ushort)256, styles.Styles);
        Assert.True(layout.TryFitMetadata(capacity, full with { HyperlinkCells = 103 }, 4, out var cells));
        Assert.Equal((ushort)384, cells.HyperlinkBytes);
        Assert.True(layout.TryFitMetadata(capacity, full with { Hyperlinks = 3 }, 4, out var links));
        Assert.Equal((ushort)384, links.HyperlinkBytes);
        Assert.True(layout.TryFitMetadata(capacity, full with { StringBytes = 2049 }, 4, out var strings));
        Assert.Equal(4096U, strings.StringBytes);
        Assert.True(layout.TryFitMetadata(capacity, full with { GraphemeCells = 2, GraphemeBytes = 32 }, 4, out var graphemes));
        Assert.True(GhosttySnapshotAllocation.GraphemeCellCapacity(graphemes.GraphemeBytes) >= 2);
        Assert.Equal(1024UL, GhosttySnapshotAllocation.BitmapDataBytes(graphemes.GraphemeBytes, 16));
    }

    [Fact]
    public void ReplacementScratchIsChargedOnceNotForEveryGrapheme()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = new(80, 4, 0, 0, 1024, 0);
        GhosttySnapshotMetadataUsage three = new(0, 3, 544, 240, 0, 0, 0);
        Assert.True(layout.TryFitMetadata(capacity, three, 4, out var unchanged));
        Assert.Equal(capacity, unchanged);
        GhosttySnapshotMetadataUsage fourth = three with { GraphemeCells = 4, GraphemeBytes = 800 };
        Assert.True(layout.TryFitMetadata(capacity, fourth, 4, out var grown));
        Assert.Equal(2048U, grown.GraphemeBytes);
    }

    [Fact]
    public void ArithmeticIsAllocationFreeAndUnrepresentableUsageDoesNotWrap()
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = new(80, 4, 128, 192, 8192, 2048);
        GhosttySnapshotMetadataUsage full = new(200, 1, 256, 240, 2, 103, 2049);
        layout.TryFitMetadata(capacity, full, 4, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) layout.TryFitMetadata(capacity, full, 4, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(layout.TryFitMetadata(capacity, full with { Styles = ulong.MaxValue }, 4, out _));
        Assert.False(layout.TryFitMetadata(capacity, full with { GraphemeBytes = ulong.MaxValue }, 4, out _));
        Assert.False(layout.TryFitMetadata(capacity, full with { HyperlinkCells = ulong.MaxValue }, 4, out _));
        Assert.False(layout.TryFitMetadata(capacity, full with { StringBytes = ulong.MaxValue }, 4, out _));
    }

    [Theory]
    [InlineData(0)] // style
    [InlineData(1)] // grapheme
    [InlineData(2)] // hyperlink and string
    public void GrowthFromRestoredZeroHintsMatchesNativeAdmission(int variant)
    {
        RequireNative();
        int alignment = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;
        GhosttySnapshotAllocation layout = new(alignment);
        using BasicVtProcessor source = new(new TerminalScreen(80, 4, 5000));
        for (int i = 0; i < 1200; i++) source.Process("\r\n"u8);
        List<SnapshotTestRecord> records = ReadRecords(source.GetBinarySnapshot());
        foreach (SnapshotTestRecord record in records)
            if (record.Tag == GhosttySnapshotRecordTag.Page)
                BinaryPrimitives.WriteUInt32LittleEndian(record.Payload.AsSpan(variant == 2 ? 12 : 16), 1_000_000);
        using GhosttySnapshotStateReader probe = new(SnapshotTestRecords.Encode(records), new());
        GhosttySnapshotPage resident = Assert.Single(probe.ReadReady().Screens[0].Pages.ToArray());
        GhosttySnapshotPage history = probe.ReadNextHistoryPage()!.Value.Page;
        GhosttySnapshotPageCapacity expected = variant switch
        {
            0 => resident.Capacity with { Styles = 16 },
            1 => resident.Capacity with { GraphemeBytes = 1024 },
            _ => resident.Capacity with { HyperlinkBytes = 192, StringBytes = 4096 },
        };
        ulong boundary = layout.AllocatedBytes(expected) + layout.AllocatedBytes(history.Capacity);
        byte[] input = Encoding.UTF8.GetBytes(variant switch
        {
            0 => "\u001b[1mA\u001b[0m",
            1 => "A" + new string('\u0301', 64),
            // Each OSC 8 must fit native's 2048-byte parser limit. Two
            // distinct live links exercise string growth past 2048 bytes.
            _ => "\u001b]8;id=one;" + new string('x', 1000) + "\u001b\\A\u001b]8;;\u001b\\" +
                "\u001b]8;id=two;" + new string('x', 1000) + "\u001b\\B\u001b]8;;\u001b\\",
        });
        foreach (bool fits in new[] { false, true })
        {
            BinaryPrimitives.WriteUInt64LittleEndian(records[0].Payload.AsSpan(87), fits ? boundary : boundary - 1);
            byte[] bytes = SnapshotTestRecords.Encode(records);
            using GhosttySnapshotDecoder nativeDecoder = new(bytes);
            using GhosttyTerminal native = nativeDecoder.Ready();
            using ManagedTerminalSnapshotDecoder managedDecoder = new(bytes);
            using ManagedTerminalSnapshot managed = managedDecoder.Ready();
            native.Write(input);
            managed.Processor.Process(input);
            _ = GhosttySnapshotLiveAllocation.Measure(managed.Screen, managed.Screen.GetSnapshotRows(0)!, layout);
            Assert.Equal(expected, managed.Screen.GetSnapshotRows(0)![0].SnapshotAllocation!.Capacity);
            using GhosttySnapshotStateReader nativeReader = new(GhosttySnapshot.Encode(native), new());
            Assert.Equal(expected, Assert.Single(nativeReader.ReadReady().Screens[0].Pages.ToArray()).Capacity);
            Assert.True(nativeDecoder.Next());
            ManagedTerminalSnapshotProgress progress = managedDecoder.Next()!.Value;
            Assert.Equal(nativeDecoder.GetProgressRows(), (nuint)progress.RowsApplied);
            Assert.Equal(fits, progress.RowsApplied > 0);
        }
    }

    private static List<SnapshotTestRecord> ReadRecords(byte[] source)
    {
        using GhosttySnapshotRecordReader reader = new(source, 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            records.Add(new(tag, payload.ToArray()));
            if (tag == GhosttySnapshotRecordTag.Finish) return records;
        }
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
