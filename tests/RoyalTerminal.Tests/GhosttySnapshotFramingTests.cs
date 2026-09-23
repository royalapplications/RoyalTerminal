// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotFramingTests
{
    [Fact]
    public void EnvelopeAndChecksum_MatchUpstreamGoldenBytes()
    {
        Assert.Equal(Fixture("envelope-v1.hex"), GhosttySnapshotFraming.Envelope.ToArray());
        using MemoryStream stream = new();
        GhosttySnapshotFraming.WriteRecord(stream, GhosttySnapshotRecordTag.Ready, []);
        // Upstream checkpoint fixture starts with an unrelated three-byte prefix.
        Assert.Equal(Fixture("checkpoint-ready-v1.hex").AsSpan(3).ToArray(), stream.ToArray());
        Assert.Equal(0xE3069283u, GhosttySnapshotFraming.ComputeChecksum([], "123456789"u8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteUpstreamFixture_ReencodesEveryRecordByteForByte(bool streamed)
    {
        byte[] bytes = Fixture("complete-v1.hex");
        using MemoryStream source = new(bytes);
        using GhosttySnapshotRecordReader reader = streamed ? new(source, 1_000_000) : new(bytes.AsMemory(), 1_000_000);
        using MemoryStream output = new();
        reader.ReadEnvelope();
        GhosttySnapshotFraming.WriteEnvelope(output);
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            GhosttySnapshotFraming.WriteRecord(output, tag, payload);
            if (tag == GhosttySnapshotRecordTag.Finish) break;
        }
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(bytes.Length, reader.SourceOffset);
    }

    [Fact]
    public void MemoryRecord_BorrowsPayloadAndAllocatesNothingPerRead()
    {
        using MemoryStream output = new();
        GhosttySnapshotFraming.WriteEnvelope(output);
        for (int i = 0; i < 1_001; i++) GhosttySnapshotFraming.WriteRecord(output, GhosttySnapshotRecordTag.Page, "abc"u8);
        byte[] bytes = output.ToArray();
        using GhosttySnapshotRecordReader reader = new(bytes.AsMemory(), 100);
        reader.ReadEnvelope();
        reader.ReadRecord(out ReadOnlySpan<byte> first);
        bytes[20] = (byte)'X'; // Proof of borrowed backing storage, after checksum validation.
        Assert.Equal((byte)'X', first[0]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++) reader.ReadRecord(out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Finish_DoesNotConsumeFollowingTransportBytesOrOwnTheStream()
    {
        using MemoryStream stream = new();
        GhosttySnapshotFraming.WriteEnvelope(stream);
        GhosttySnapshotFraming.WriteRecord(stream, GhosttySnapshotRecordTag.Finish, []);
        stream.Write("tail"u8);
        stream.Position = 0;
        using (GhosttySnapshotRecordReader reader = new(stream, 100))
        {
            reader.ReadEnvelope();
            Assert.Equal(GhosttySnapshotRecordTag.Finish, reader.ReadRecord(out _));
            Assert.Throws<InvalidOperationException>(() => reader.ReadRecord(out _));
        }
        Assert.Equal((int)'t', stream.ReadByte());
    }

    [Fact]
    public void Corruption_IsRejectedAndReaderCannotResume()
    {
        using MemoryStream stream = new();
        GhosttySnapshotFraming.WriteEnvelope(stream);
        GhosttySnapshotFraming.WriteRecord(stream, GhosttySnapshotRecordTag.Page, "payload"u8);
        byte[] bytes = stream.ToArray();
        bytes[^1] ^= 0x01;
        using GhosttySnapshotRecordReader reader = new(bytes.AsMemory(), 100);
        reader.ReadEnvelope();
        Assert.Throws<InvalidDataException>(() => reader.ReadRecord(out _));
        Assert.Throws<InvalidOperationException>(() => reader.ReadRecord(out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    [InlineData(3, int.MaxValue)]
    [InlineData(5, 1)]
    [InlineData(6, 1)]
    public void InvalidOrOversizedHeader_IsRejectedBeforePayloadAllocation(int tag, int length)
    {
        byte[] bytes = new byte[20];
        GhosttySnapshotFraming.Envelope.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)tag);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)length);
        using GhosttySnapshotRecordReader reader = new(bytes.AsMemory(), 1024);
        reader.ReadEnvelope();
        Assert.Throws<InvalidDataException>(() => reader.ReadRecord(out _));
    }

    [Fact]
    public void TruncatedInput_IsRejectedAtEveryBoundary()
    {
        using MemoryStream output = new();
        GhosttySnapshotFraming.WriteEnvelope(output);
        GhosttySnapshotFraming.WriteRecord(output, GhosttySnapshotRecordTag.Page, "payload"u8);
        byte[] bytes = output.ToArray();
        for (int length = 0; length < bytes.Length; length++)
        {
            using GhosttySnapshotRecordReader reader = new(bytes.AsMemory(0, length), 100);
            Assert.Throws<EndOfStreamException>(() =>
            {
                reader.ReadEnvelope();
                reader.ReadRecord(out _);
            });
        }
    }

    internal static byte[] Fixture(string name)
    {
        StringBuilder hex = new();
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GhosttySnapshot", name)))
        {
            ReadOnlySpan<char> content = line.AsSpan();
            int comment = content.IndexOf('#');
            if (comment >= 0) content = content[..comment];
            foreach (char value in content)
                if (!char.IsWhiteSpace(value)) hex.Append(value);
        }
        return Convert.FromHexString(hex.ToString());
    }
}
