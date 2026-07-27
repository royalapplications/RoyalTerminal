// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class UnixPtyReadBatchPolicyTests
{
    [Fact]
    public void ShouldDispatchAfterRead_TreatsShortReadAsInteractive()
    {
        Assert.True(UnixPtyReadBatchPolicy.ShouldDispatchAfterRead(1));
        Assert.True(UnixPtyReadBatchPolicy.ShouldDispatchAfterRead(UnixPtyReadBatchPolicy.BridgeThreshold - 1));
        Assert.False(UnixPtyReadBatchPolicy.ShouldDispatchAfterRead(UnixPtyReadBatchPolicy.BridgeThreshold));
        Assert.False(UnixPtyReadBatchPolicy.ShouldDispatchAfterRead(UnixPtyReadBatchPolicy.BridgeThreshold + 1));
    }

    [Fact]
    public void ShouldBridgeAfterWouldBlock_RequiresSaturatedBatch()
    {
        Assert.False(UnixPtyReadBatchPolicy.ShouldBridgeAfterWouldBlock(0));
        Assert.False(UnixPtyReadBatchPolicy.ShouldBridgeAfterWouldBlock(UnixPtyReadBatchPolicy.BridgeThreshold - 1));
        Assert.True(UnixPtyReadBatchPolicy.ShouldBridgeAfterWouldBlock(UnixPtyReadBatchPolicy.BridgeThreshold));
        Assert.True(UnixPtyReadBatchPolicy.ShouldBridgeAfterWouldBlock(UnixPtyReadBatchPolicy.BufferCapacity));
    }

    [Fact]
    public void IsGatherBudgetExpired_ExpiresAfterThreeMilliseconds()
    {
        long start = Stopwatch.GetTimestamp();
        long beforeBudget = start + Math.Max(1, (long)(Stopwatch.Frequency * 0.001));
        long afterBudget = start + Math.Max(1, (long)(Stopwatch.Frequency * 0.004));

        Assert.False(UnixPtyReadBatchPolicy.IsGatherBudgetExpired(start, beforeBudget));
        Assert.True(UnixPtyReadBatchPolicy.IsGatherBudgetExpired(start, afterBudget));
    }
}
