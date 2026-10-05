// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyImageBufferTests
{
    [Fact]
    public void DirectOwnedPayloadCanBeTransferredWithoutCopying()
    {
        byte[] data = [1, 2, 3];
        ManagedKittyImageBuffer buffer = new(3, data);
        Assert.Same(data, buffer.Take(3));
        Assert.True(buffer.Data.IsEmpty);
    }

    [Fact]
    public void GrowthAndFailureRespectLimitWithoutMutatingExistingData()
    {
        ManagedKittyImageBuffer buffer = new(5);
        Assert.True(buffer.TryAppend([1, 2, 3]));
        Assert.False(buffer.TryAppend([4, 5, 6]));
        Assert.True(buffer.TryAppend([4, 5]));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer.Data.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer.Take(3));
    }

    [Fact]
    public void ZeroLimitSupportsEmptyPayloadOnly()
    {
        ManagedKittyImageBuffer buffer = new(0);
        Assert.True(buffer.TryAppend([]));
        Assert.False(buffer.TryAppend([1]));
        Assert.Empty(buffer.Take(0));
    }

    [Theory]
    [InlineData((int)ManagedKittyImageAllocation.BufferGrowth)]
    [InlineData((int)ManagedKittyImageAllocation.BufferTransfer)]
    public void FailedReserveOrExactSizeCopyPreservesTheBuffer(int checkpoint)
    {
        bool fail = true;
        ManagedKittyImageBuffer buffer = new(8, new byte[] { 1, 2 });
        Action<ManagedKittyImageAllocation> allocationCheckpoint = stage =>
        {
            if (fail && (int)stage == checkpoint) throw new OutOfMemoryException();
        };
        if (checkpoint == (int)ManagedKittyImageAllocation.BufferGrowth)
        {
            Assert.Throws<OutOfMemoryException>(() => buffer.TryAppend([3, 4], allocationCheckpoint));
            Assert.Equal(new byte[] { 1, 2 }, buffer.Data.ToArray());
            fail = false;
            Assert.True(buffer.TryAppend([3, 4], allocationCheckpoint));
        }
        else
        {
            Assert.True(buffer.TryAppend([3, 4], allocationCheckpoint));
            Assert.Throws<OutOfMemoryException>(() => buffer.Take(4, allocationCheckpoint));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.Data.ToArray());
            fail = false;
        }
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.Take(4, allocationCheckpoint));
        Assert.True(buffer.Data.IsEmpty);
    }

    [Fact]
    public void DefaultValueIsAnEmptyZeroLimitBuffer()
    {
        ManagedKittyImageBuffer buffer = default;
        Assert.True(buffer.Data.IsEmpty);
        Assert.True(buffer.TryAppend([]));
        Assert.False(buffer.TryAppend([1]));
        Assert.Empty(buffer.Take(0));
    }

    [Fact]
    public void OwnedPayloadTransferDoesNotAllocateABufferWrapper()
    {
        byte[] data = [1, 2, 3, 4];
        _ = MeasureTransfers(data, 100);
        Assert.Equal(0, MeasureTransfers(data, 1000));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureTransfers(byte[] data, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            ManagedKittyImageBuffer buffer = new(data.Length, data);
            if (!ReferenceEquals(data, buffer.Take(data.Length))) throw new InvalidOperationException("Lost owned buffer");
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
