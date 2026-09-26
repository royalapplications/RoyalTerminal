// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class UnixPtyOutputRingTests
{
    [Fact]
    public async Task Ring_BoundsInFlightStorage_AndIgnoresRepeatedLeaseDisposal()
    {
        UnixPtyOutputRing ring = new(static () => { });
        TerminalOutputLease[] leases = new TerminalOutputLease[4];
        for (int index = 0; index < leases.Length; index++)
        {
            UnixPtyOutputRing.Slot slot = Assert.IsType<UnixPtyOutputRing.Slot>(ring.Acquire());
            Assert.Equal(64 * 1024, slot.Buffer.Length);
            leases[index] = slot.CreateLease(1);
        }

        try
        {
            Task<UnixPtyOutputRing.Slot?> pending = Task.Run(ring.Acquire);
            await Task.Delay(25);
            Assert.False(pending.IsCompleted);
            leases[0].Dispose();
            UnixPtyOutputRing.Slot reused = Assert.IsType<UnixPtyOutputRing.Slot>(
                await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            TerminalOutputLease current = reused.CreateLease(1);

            leases[0].Dispose();
            Task<UnixPtyOutputRing.Slot?> stillFull = Task.Run(ring.Acquire);
            await Task.Delay(25);
            Assert.False(stillFull.IsCompleted);
            current.Dispose();
            UnixPtyOutputRing.Slot returned = Assert.IsType<UnixPtyOutputRing.Slot>(
                await stillFull.WaitAsync(TimeSpan.FromSeconds(5)));
            returned.CreateLease(0).Dispose();
        }
        finally
        {
            ring.Stop();
            foreach (TerminalOutputLease lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    [Fact]
    public async Task Stop_UnblocksGatherWaitingForFreeStorage()
    {
        UnixPtyOutputRing ring = new(static () => { });
        for (int index = 0; index < UnixPtyOutputRing.BufferCount; index++)
        {
            Assert.NotNull(ring.Acquire());
        }

        Task<UnixPtyOutputRing.Slot?> pending = Task.Run(ring.Acquire);
        ring.Stop();
        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ParserIdle_WakesOnlyAnArmedBridge()
    {
        int wakeCount = 0;
        UnixPtyOutputRing ring = new(() => wakeCount++);
        TerminalOutputLease parsed = ring.Acquire()!.CreateLease(1);
        TerminalOutputLease filling = ring.Acquire()!.CreateLease(1);

        Assert.True(ring.TryBeginBridge());
        parsed.Dispose();
        Assert.Equal(1, wakeCount);
        ring.EndBridge();
        Assert.False(ring.TryBeginBridge());
        filling.Dispose();
        Assert.Equal(1, wakeCount);
    }
}
