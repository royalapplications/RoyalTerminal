// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotMetadataTests
{
    [Fact]
    public void Style_MatchesUpstreamGoldenWithoutResolvingPaletteIdentity()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("style-v1.hex");
        GhosttySnapshotStyle style = GhosttySnapshotStyle.Read(bytes)!.Value;
        Assert.Equal(default, style.Foreground);
        Assert.Equal(new GhosttySnapshotColor(1, 0x7F, 0, 0), style.Background);
        Assert.Equal(new GhosttySnapshotColor(2, 0x12, 0x34, 0x56), style.UnderlineColor);
        Assert.Equal(0x3FF, style.Flags);
        Span<byte> output = stackalloc byte[16];
        style.Write(output);
        Assert.Equal(bytes, output.ToArray());
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++) GhosttySnapshotStyle.Read(bytes)!.Value.Write(output);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 1)]
    [InlineData(6, 1)]
    [InlineData(13, 6)]
    [InlineData(13, 8)]
    [InlineData(14, 1)]
    [InlineData(15, 1)]
    public void Style_InvalidSemanticEntryCanBeDiscarded(int offset, byte value)
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("style-v1.hex");
        bytes[offset] = value;
        Assert.Null(GhosttySnapshotStyle.Read(bytes));
    }

    [Fact]
    public void Style_RejectsEveryTruncationAndInvalidEncodeBeforeWriting()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("style-v1.hex");
        for (int length = 0; length < 16; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotStyle.Read(bytes.AsSpan(0, length)));
        byte[] destination = Enumerable.Repeat((byte)0xA5, 16).ToArray();
        GhosttySnapshotStyle invalid = new(default, default, default, 0x8000);
        Assert.Throws<InvalidDataException>(() => invalid.Write(destination));
        Assert.All(destination, value => Assert.Equal(0xA5, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hyperlink_MatchesGoldenAndRejectsEveryTruncation(bool explicitId)
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture($"hyperlink-{(explicitId ? "explicit" : "implicit")}-v1.hex");
        GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(bytes, out int consumed);
        Assert.True(link.IsValid);
        Assert.Equal(explicitId, link.HasExplicitId);
        Assert.Equal(bytes.Length, consumed);
        using MemoryStream output = new();
        link.WriteTo(output);
        Assert.Equal(bytes, output.ToArray());
        for (int length = 0; length < bytes.Length; length++)
            Assert.Throws<EndOfStreamException>(() => ReadHyperlink(bytes.AsSpan(0, length)));
    }

    [Fact]
    public void Hyperlink_PreservesArbitraryBytesAndBorrowsSourceWithoutAllocating()
    {
        byte[] bytes = [2, 2, 0, 0, 0, 0xFF, 0, 3, 0, 0, 0, 0xFE, 0x81, 0];
        GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(bytes, out _);
        bytes[5] = 0xFD;
        Assert.Equal(0xFD, link.ExplicitId[0]);
        using MemoryStream output = new();
        link.WriteTo(output);
        Assert.Equal(bytes, output.ToArray());
        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int i = 0; i < 1_000; i++) total += GhosttySnapshotHyperlink.Read(bytes, out _).Uri.Length;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(3_000, total);
    }

    [Fact]
    public void Hyperlink_InvalidStringsConsumeWholeEntryButBadKindsAndLengthsAreFatal()
    {
        byte[] emptyId = [2, 0, 0, 0, 0, 1, 0, 0, 0, (byte)'u', 0xAA];
        GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(emptyId, out int consumed);
        Assert.False(link.IsValid);
        Assert.Equal(10, consumed);
        Assert.Throws<InvalidDataException>(() => ReadHyperlink([3, 0, 0, 0, 0]));
        Assert.Throws<EndOfStreamException>(() => ReadHyperlink([2, 255, 255, 255, 255]));
        Assert.Throws<EndOfStreamException>(() => ReadHyperlink([1, 0, 0, 0, 0, 255, 255, 255, 255]));
        using MemoryStream output = new();
        Assert.Throws<InvalidDataException>(() => WriteInvalidHyperlink(output));
        Assert.Equal(0, output.Length);
    }

    private static void ReadHyperlink(ReadOnlySpan<byte> bytes) => GhosttySnapshotHyperlink.Read(bytes, out _);

    private static void WriteInvalidHyperlink(Stream destination)
        => new GhosttySnapshotHyperlink(true, 0, [], "uri"u8).WriteTo(destination);
}
