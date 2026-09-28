// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotStateReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoldenReadyAndIncrementalHistoryAreOwnedAndStopBeforeTransportData(bool streamed)
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        byte[] source = [.. fixture, 0xAB, 0xCD];
        using MemoryStream stream = new(source);
        using GhosttySnapshotStateReader reader = streamed ? new(stream, new()) : new(source, new());
        Assert.Throws<InvalidOperationException>(() => reader.ReadNextHistoryPage());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        Assert.Equal(0x4BE, reader.SourceOffset);
        Assert.Equal((2, 3, 2), (ready.Terminal.Header.Columns, ready.Terminal.Header.Rows, ready.Screens.Length));
        Assert.Empty(ready.Continuation.ToArray());
        Assert.Equal(0, ready.Screens[0].State.Key);
        Assert.Equal(1, ready.Screens[1].State.Key);
        Assert.Equal(1, ready.Screens[0].Pages.Length);
        Assert.Equal(1, ready.Screens[1].Pages.Length);
        ulong firstCell = ready.Screens[0].Pages[0].Grid.Cells[0];
        Assert.Throws<InvalidOperationException>(() => reader.ReadReady());
        GhosttySnapshotHistoryPage first = reader.ReadNextHistoryPage()!.Value;
        GhosttySnapshotHistoryPage second = reader.ReadNextHistoryPage()!.Value;
        Assert.Equal((0, 1u), (first.Key, first.Remaining));
        Assert.Equal((0, 0u), (second.Key, second.Remaining));
        Assert.Null(reader.ReadNextHistoryPage());
        Assert.Null(reader.ReadNextHistoryPage());
        Assert.Equal(fixture.Length, reader.SourceOffset);
        if (streamed)
        {
            Assert.Equal(fixture.Length, stream.Position);
            Assert.Equal(0xAB, stream.ReadByte());
        }
        Array.Clear(source);
        Assert.Equal(firstCell, ready.Screens[0].Pages[0].Grid.Cells[0]);
        Assert.Equal(2, ready.Terminal.Header.Columns);
        reader.Dispose();
        Assert.True(stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => reader.ReadReady());
        Assert.Throws<ObjectDisposedException>(() => reader.ReadNextHistoryPage());
    }

    [Fact]
    public void EveryTruncatedSnapshotFailsAndInvalidatesTheReader()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        for (int length = 0; length < fixture.Length; length++)
        {
            using GhosttySnapshotStateReader reader = new(fixture.AsMemory(0, length), new());
            Assert.Throws<EndOfStreamException>(() => ReadAll(reader));
            Assert.Throws<InvalidOperationException>(() => reader.ReadNextHistoryPage());
        }
    }

    [Fact]
    public void MissingRecordsDuplicateKeysAndMisroutedHistoryAreRejected()
    {
        List<SnapshotTestRecord> original = SnapshotTestRecords.Fixture();
        for (int index = 0; index < original.Count; index++)
        {
            List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture(); records.RemoveAt(index);
            using GhosttySnapshotStateReader reader = new(SnapshotTestRecords.Encode(records), new());
            Exception? failure = Record.Exception(() => ReadAll(reader));
            Assert.True(failure is InvalidDataException or EndOfStreamException, failure?.ToString());
            Assert.Throws<InvalidOperationException>(() => reader.ReadReady());
        }
        foreach ((int index, ushort key) in new[] { (3, (ushort)0), (1, (ushort)2), (7, (ushort)2), (10, (ushort)0) })
        {
            List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
            BinaryPrimitives.WriteUInt16LittleEndian(records[index].Payload, key);
            using GhosttySnapshotStateReader reader = new(SnapshotTestRecords.Encode(records), new());
            Assert.Throws<InvalidDataException>(() => ReadAll(reader));
        }
        List<SnapshotTestRecord> undeclared = SnapshotTestRecords.Fixture();
        BinaryPrimitives.WriteUInt16LittleEndian(undeclared[0].Payload.AsSpan(23), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(undeclared[1].Payload, 1);
        using GhosttySnapshotStateReader oneScreen = new(SnapshotTestRecords.Encode(undeclared), new());
        Assert.Throws<InvalidDataException>(() => oneScreen.ReadReady());
    }

    [Fact]
    public void BothScreenAndHistoryGroupsAcceptEitherUniqueKeyOrder()
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        List<SnapshotTestRecord> reordered = [records[0], records[3], records[4], records[1], records[2],
            records[5], records[6], records[10], records[7], records[8], records[9], records[11]];
        using GhosttySnapshotStateReader reader = new(SnapshotTestRecords.Encode(reordered), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        Assert.Equal(0, ready.Screens[0].State.Key);
        Assert.Equal(1, ready.Screens[1].State.Key);
        Assert.Equal(0, reader.ReadNextHistoryPage()!.Value.Key);
        Assert.Equal(0, reader.ReadNextHistoryPage()!.Value.Key);
        Assert.Null(reader.ReadNextHistoryPage());
    }

    [Fact]
    public void AggregateCellsPagesAndPayloadBudgetsCannotResetPerRecord()
    {
        byte[] source = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using GhosttySnapshotStateReader cells = new(source, new(MaximumCells: 12));
        cells.ReadReady();
        Assert.Throws<InvalidDataException>(() => cells.ReadNextHistoryPage());
        using GhosttySnapshotStateReader pages = new(source, new(MaximumPages: 2));
        pages.ReadReady();
        Assert.Throws<InvalidDataException>(() => pages.ReadNextHistoryPage());
        using GhosttySnapshotStateReader bytes = new(source, new(MaximumTotalPayloadBytes: 1000));
        Assert.Throws<InvalidDataException>(() => bytes.ReadReady());
        using GhosttySnapshotStateReader strings = new(source, new(MaximumStringBytesPerRecord: 0));
        Assert.Throws<InvalidDataException>(() => strings.ReadReady());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GhosttySnapshotStateReader(source, new(MaximumPages: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GhosttySnapshotStateReader(source, new(MaximumTotalPayloadBytes: -1)));
    }

    [Fact]
    public void ScreenMustCoverActiveRowsButCanContainDifferentPhysicalWidths()
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        BinaryPrimitives.WriteUInt16LittleEndian(records[0].Payload.AsSpan(2), 4);
        using GhosttySnapshotStateReader tooShort = new(SnapshotTestRecords.Encode(records), new());
        Assert.Throws<InvalidDataException>(() => tooShort.ReadReady());
        records = SnapshotTestRecords.Fixture();
        // Wider terminal geometry is valid: page widths survive an interrupted reflow.
        BinaryPrimitives.WriteUInt16LittleEndian(records[0].Payload, 3);
        using GhosttySnapshotStateReader mixed = new(SnapshotTestRecords.Encode(records), new());
        GhosttySnapshotReadyState ready = mixed.ReadReady();
        Assert.Equal(3, ready.Terminal.Header.Columns);
        Assert.Equal(2, ready.Screens[0].Pages[0].Grid.Columns);
    }

    [Theory]
    [InlineData("\u001b[31", true)]
    [InlineData("\u001b[31m", false)]
    [InlineData("\u001b[31\u0007", false)]
    [InlineData("text\u001b[31", false)]
    public void ContinuationIsValidatedBeforeReady(string continuation, bool valid)
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(continuation);
        records[5] = new(GhosttySnapshotRecordTag.Continuation, bytes);
        using GhosttySnapshotStateReader reader = new(SnapshotTestRecords.Encode(records), new());
        if (valid) Assert.Equal(bytes, reader.ReadReady().Continuation.ToArray());
        else Assert.Throws<InvalidDataException>(() => reader.ReadReady());
        using GhosttySnapshotStateReader limited = new(SnapshotTestRecords.Encode(records), new(MaximumContinuationBytes: 1));
        Assert.Throws<InvalidDataException>(() => limited.ReadReady());
    }

    [Fact]
    public void FailedHistoryDoesNotInvalidatePreviouslyOwnedReadyState()
    {
        byte[] source = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        source[^1] ^= 1; // FINISH CRC, after all successful active/history pages.
        using GhosttySnapshotStateReader reader = new(source, new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        Assert.NotNull(reader.ReadNextHistoryPage());
        Assert.NotNull(reader.ReadNextHistoryPage());
        Assert.Throws<InvalidDataException>(() => reader.ReadNextHistoryPage());
        Assert.Equal(2, ready.Screens.Length);
        Assert.Throws<InvalidOperationException>(() => reader.ReadNextHistoryPage());
    }

    [Fact]
    public void ShortNonSeekableReadsAndIoFailuresPreserveOwnershipAndFailureState()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using FragmentedStream source = new([.. fixture, 0xEE]);
        using (GhosttySnapshotStateReader reader = new(source, new()))
        {
            ReadAll(reader);
            Assert.Equal(fixture.Length, reader.SourceOffset);
        }
        Assert.Equal(0xEE, source.ReadByte());
        using FragmentedStream failing = new(fixture, failAt: 0x4BE);
        using GhosttySnapshotStateReader failed = new(failing, new());
        GhosttySnapshotReadyState ready = failed.ReadReady();
        Assert.Throws<IOException>(() => failed.ReadNextHistoryPage());
        Assert.Throws<InvalidOperationException>(() => failed.ReadNextHistoryPage());
        Assert.Equal(2, ready.Screens.Length);
    }

    internal static void ReadAll(GhosttySnapshotStateReader reader)
    {
        reader.ReadReady();
        while (reader.ReadNextHistoryPage() is not null) { }
    }

    private sealed class FragmentedStream(byte[] bytes, int failAt = int.MaxValue) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_offset >= failAt) throw new IOException("Injected transport failure.");
            int count = Math.Min(Math.Min(3, buffer.Length), Math.Min(bytes.Length - _offset, failAt - _offset));
            bytes.AsSpan(_offset, count).CopyTo(buffer); _offset += count; return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal readonly record struct SnapshotTestRecord(GhosttySnapshotRecordTag Tag, byte[] Payload);

internal static class SnapshotTestRecords
{
    internal static List<SnapshotTestRecord> Fixture()
    {
        using GhosttySnapshotRecordReader reader = new(GhosttySnapshotFramingTests.Fixture("complete-v1.hex"), 1_000_000);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            records.Add(new(tag, payload.ToArray()));
            if (tag == GhosttySnapshotRecordTag.Finish) return records;
        }
    }

    internal static byte[] Encode(List<SnapshotTestRecord> records)
    {
        using MemoryStream output = new(); output.Write(GhosttySnapshotFraming.Envelope);
        foreach (SnapshotTestRecord record in records) GhosttySnapshotFraming.WriteRecord(output, record.Tag, record.Payload);
        return output.ToArray();
    }
}
