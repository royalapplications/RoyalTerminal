// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittySnapshotValidationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PayloadLengthIsValidatedOnlyForMatchingPendingTargets(bool extra, bool discarded)
    {
        byte[] bytes = Rewrite("kitty-placed-v1.hex", (tag, payload) => tag != GhosttySnapshotRecordTag.KittyImagePayload
            ? payload : extra ? [.. payload, 0] : payload[..^1]);
        using ManagedTerminalSnapshotDecoder decoder = new(bytes, new()
        { ProcessorOptions = new() { KittyGraphicsStorageLimitBytes = discarded ? 0 : 1024 * 1024 } });
        using ManagedTerminalSnapshot restored = decoder.Ready();
        if (discarded)
        {
            Assert.Equal(0, decoder.Next()!.Value.ImageBytesApplied);
            Assert.Null(decoder.Next());
        }
        else
        {
            if (extra) Assert.Throws<InvalidDataException>(() => decoder.Next());
            else Assert.Throws<EndOfStreamException>(() => decoder.Next());
            Assert.Throws<InvalidOperationException>(() => decoder.Next());
            restored.Processor.Process("still usable"u8);
        }
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttySnapshotDecoder nativeDecoder = new(bytes);
        nativeDecoder.SetKittyImageStorageLimit(discarded ? 0ul : 1024 * 1024);
        using GhosttyTerminal native = nativeDecoder.Ready();
        if (discarded)
        {
            Assert.True(nativeDecoder.Next()); Assert.Equal((nuint?)0, nativeDecoder.GetProgressImageBytes());
            Assert.False(nativeDecoder.Next());
        }
        else Assert.Throws<InvalidOperationException>(() => nativeDecoder.Next());
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(ulong.MaxValue)]
    public void WrongDeclaredPixelLengthIsDiscardedWithoutChangingPendingReservation(ulong declared)
    {
        byte[] bytes = Rewrite("kitty-placed-v1.hex", (tag, payload) =>
        {
            if (tag == GhosttySnapshotRecordTag.KittyImagePayload) BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(10), declared);
            return payload;
        });
        using ManagedTerminalSnapshotDecoder decoder = new(bytes);
        using ManagedTerminalSnapshot restored = decoder.Ready();
        Assert.Equal(0, decoder.Next()!.Value.ImageBytesApplied);
        Assert.Empty(restored.Screen.GetKittyPlacements().ToArray());
        Assert.Null(decoder.Next());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SemanticallyInvalidImageMetadataIsDroppedAndLaterRecordsStayAligned(int corruption)
    {
        byte[] bytes = Rewrite("kitty-animation-v1.hex", (tag, payload) =>
        {
            if (tag != GhosttySnapshotRecordTag.KittyImageStorage) return payload;
            GhosttySnapshotKittyStorage storage = GhosttySnapshotKittyCodec.ReadStorage(payload);
            GhosttySnapshotKittyImage image = storage.Images[0];
            image = corruption switch
            {
                0 => image with { Id = 0 },
                1 => image with { Format = 2 },
                2 => image with { Width = uint.MaxValue, Height = uint.MaxValue, Length = ulong.MaxValue },
                _ => image with { Animation = image.Animation! with { Frames = [new(1, ulong.MaxValue)] } },
            };
            storage = storage with { Images = [image] };
            GhosttySnapshotRecordBuffer writer = new(stackalloc byte[256], 4096);
            try { GhosttySnapshotKittyCodec.WriteStorage(ref writer, storage); return writer.WrittenSpan.ToArray(); }
            finally { writer.Dispose(); }
        });
        using ManagedTerminalSnapshotDecoder decoder = new(bytes);
        using ManagedTerminalSnapshot restored = decoder.Ready();
        int steps = 0;
        while (decoder.Next() is { } progress) { Assert.Equal(0, progress.ImageBytesApplied); steps++; }
        Assert.Equal(3, steps); Assert.Empty(restored.Screen.GetKittyPlacements().ToArray());
        if (GhosttyVtProcessor.IsAvailable())
        {
            using GhosttyTerminal native = GhosttySnapshot.Decode(bytes);
            Assert.True(native.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
            Assert.False(graphics!.TryGetImage(5, out _));
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(14)]
    public void MalformedStorageCountsFailBeforeAllocation(int offset)
    {
        byte[] bytes = Rewrite("kitty-placed-v1.hex", (tag, payload) =>
        {
            if (tag == GhosttySnapshotRecordTag.KittyImageStorage) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), uint.MaxValue);
            return payload;
        });
        Assert.Throws<EndOfStreamException>(() => ManagedTerminalSnapshot.Restore(bytes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidPlacementNamespacesOrExternalZeroIdsAreDropped(int tag)
    {
        byte[] bytes = Rewrite("kitty-relative-v1.hex", (record, payload) =>
        {
            if (record != GhosttySnapshotRecordTag.KittyImageStorage) return payload;
            GhosttySnapshotKittyStorage storage = GhosttySnapshotKittyCodec.ReadStorage(payload);
            storage = storage with { Placements = storage.Placements.Select(p => p with
            { Key = p.Key with { Tag = (byte)tag, Id = 0 } }).ToArray() };
            GhosttySnapshotRecordBuffer writer = new(stackalloc byte[256], 4096);
            try { GhosttySnapshotKittyCodec.WriteStorage(ref writer, storage); return writer.WrittenSpan.ToArray(); }
            finally { writer.Dispose(); }
        });
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes);
        Assert.Empty(restored.Screen.GetKittyPlacements().ToArray());
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyTerminal native = GhosttySnapshot.Decode(bytes);
        Assert.True(native.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        using GhosttyKittyGraphicsPlacementIterator iterator = graphics!.CreatePlacementIterator();
        iterator.Populate(graphics); Assert.False(iterator.MoveNext());
    }

    [Theory]
    [InlineData("\x1b" + "c")]
    [InlineData("\x1b_Ga=d,d=I,i=3\x1b\\")]
    public void ResetOrImageDeletionRetiresLatePixelTickets(string input)
    {
        using ManagedTerminalSnapshotDecoder decoder = new(GhosttySnapshotFramingTests.Fixture("kitty-placed-v1.hex"));
        using ManagedTerminalSnapshot restored = decoder.Ready();
        using GhosttySnapshotStateReader reader = new(restored.Processor.GetBinarySnapshot(), new());
        uint id = reader.ReadReady().Screens[0].Kitty!.Images[0].Id;
        if (input.Contains("i=3", StringComparison.Ordinal)) input = input.Replace("i=3", $"i={id}", StringComparison.Ordinal);
        restored.Processor.Process(System.Text.Encoding.ASCII.GetBytes(input));
        Assert.Equal(0, decoder.Next()!.Value.ImageBytesApplied);
        Assert.Empty(restored.Screen.GetKittyPlacements().ToArray());
    }

    internal static byte[] Rewrite(string fixture, Func<GhosttySnapshotRecordTag, byte[], byte[]> transform)
    {
        using GhosttySnapshotRecordReader reader = new(GhosttySnapshotFramingTests.Fixture(fixture), int.MaxValue);
        using MemoryStream output = new();
        reader.ReadEnvelope(); GhosttySnapshotFraming.WriteEnvelope(output);
        GhosttySnapshotRecordTag tag;
        do
        {
            tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            GhosttySnapshotFraming.WriteRecord(output, tag, transform(tag, payload.ToArray()));
        } while (tag != GhosttySnapshotRecordTag.Finish);
        return output.ToArray();
    }
}
