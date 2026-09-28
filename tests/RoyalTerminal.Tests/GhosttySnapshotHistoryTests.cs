// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotHistoryTests
{
    [Fact]
    public void HistoryMatchesGoldenAndRejectsStructuralErrorsAndResourceLimits()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("history-header-v1.hex");
        GhosttySnapshotHistoryHeader header = GhosttySnapshotHistoryHeader.Read(golden, int.MaxValue);
        byte[] output = new byte[6]; header.Write(output);
        Assert.Equal(golden, output);
        for (int length = 0; length < golden.Length; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotHistoryHeader.Read(golden.AsSpan(0, length), int.MaxValue));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotHistoryHeader.Read([.. golden, 0], int.MaxValue));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotHistoryHeader.Read(golden, 0));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotHistoryHeader.Read([2, 0, 0, 0, 0, 0], 0));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotHistoryHeader.Read([0, 0, 255, 255, 255, 255], int.MaxValue));
        Assert.Throws<ArgumentException>(() => header.Write(new byte[5]));
        Assert.Throws<InvalidDataException>(() => (header with { Key = 2 }).Write(output));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyHistoryStillRoutesToItsScreen(ushort key)
    {
        GhosttySnapshotHistoryHeader header = new(key, 0);
        byte[] bytes = new byte[6]; header.Write(bytes);
        Assert.Equal(header, GhosttySnapshotHistoryHeader.Read(bytes, 0));
        for (int i = 0; i < 100; i++) header.Write(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            header.Write(bytes);
            _ = GhosttySnapshotHistoryHeader.Read(bytes, 0);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
