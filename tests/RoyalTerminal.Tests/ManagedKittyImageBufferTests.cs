// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
}
