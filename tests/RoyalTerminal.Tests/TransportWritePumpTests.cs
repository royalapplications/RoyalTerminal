// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TransportWritePumpTests
{
    [Fact]
    public void WritePump_Enqueue_DoesNotBlockWhilePreviousWriteIsActive()
    {
        ManualResetEventSlim firstWriteStarted = new(false);
        ManualResetEventSlim allowWriteCompletion = new(false);

        using TransportWritePump pump = new(
            "TransportWritePumpTests.NonBlocking",
            payload =>
            {
                _ = payload;
                firstWriteStarted.Set();
                Assert.True(allowWriteCompletion.Wait(TimeSpan.FromSeconds(5)));
            });

        Assert.True(pump.TryEnqueue("first"u8.ToArray()));
        Assert.True(firstWriteStarted.Wait(TimeSpan.FromSeconds(5)));

        Stopwatch enqueueLatency = Stopwatch.StartNew();
        Assert.True(pump.TryEnqueue("second"u8.ToArray()));
        enqueueLatency.Stop();

        allowWriteCompletion.Set();
        pump.RequestStop(discardPendingWrites: false);
        Assert.True(pump.Join(TimeSpan.FromSeconds(5)));
        Assert.True(
            enqueueLatency.Elapsed < TimeSpan.FromMilliseconds(100),
            $"Expected enqueue to return quickly while the write pump was busy, but it took {enqueueLatency.Elapsed}.");
    }

    [Fact]
    public async Task WritePump_PrioritizesUrgentControlBytesOverQueuedWrites()
    {
        ManualResetEventSlim firstWriteStarted = new(false);
        ManualResetEventSlim releaseFirstWrite = new(false);
        object writesSync = new();
        List<byte[]> writes = [];
        int writeCount = 0;

        using TransportWritePump pump = new(
            "TransportWritePumpTests.Priority",
            payload =>
            {
                int currentWrite = Interlocked.Increment(ref writeCount);
                lock (writesSync)
                {
                    writes.Add(payload);
                }

                if (currentWrite == 1)
                {
                    firstWriteStarted.Set();
                    Assert.True(releaseFirstWrite.Wait(TimeSpan.FromSeconds(5)));
                }
            });

        Assert.True(pump.TryEnqueue("normal-1"u8.ToArray()));
        Assert.True(firstWriteStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(pump.TryEnqueue("normal-2"u8.ToArray()));
        Assert.True(pump.TryEnqueue([0x03]));

        releaseFirstWrite.Set();

        bool drained = await WaitUntilAsync(
            () =>
            {
                lock (writesSync)
                {
                    return writes.Count == 3;
                }
            },
            TimeSpan.FromSeconds(5));

        Assert.True(drained, "Expected the write pump to drain queued writes.");
        pump.RequestStop(discardPendingWrites: false);
        Assert.True(pump.Join(TimeSpan.FromSeconds(5)));

        lock (writesSync)
        {
            Assert.Equal("normal-1", System.Text.Encoding.UTF8.GetString(writes[0]));
            Assert.Equal(new byte[] { 0x03 }, writes[1]);
            Assert.Equal("normal-2", System.Text.Encoding.UTF8.GetString(writes[2]));
        }
    }

    [Fact]
    public async Task WritePump_PrioritizesFormFeedPromptRedrawOverQueuedWrites()
    {
        ManualResetEventSlim firstWriteStarted = new(false);
        ManualResetEventSlim releaseFirstWrite = new(false);
        object writesSync = new();
        List<byte[]> writes = [];
        int writeCount = 0;

        using TransportWritePump pump = new(
            "TransportWritePumpTests.FormFeedPriority",
            payload =>
            {
                int currentWrite = Interlocked.Increment(ref writeCount);
                lock (writesSync)
                {
                    writes.Add(payload);
                }

                if (currentWrite == 1)
                {
                    firstWriteStarted.Set();
                    Assert.True(releaseFirstWrite.Wait(TimeSpan.FromSeconds(5)));
                }
            });

        Assert.True(pump.TryEnqueue("normal-1"u8.ToArray()));
        Assert.True(firstWriteStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(pump.TryEnqueue("normal-2"u8.ToArray()));
        Assert.True(pump.TryEnqueue([0x0C]));

        releaseFirstWrite.Set();

        bool drained = await WaitUntilAsync(
            () =>
            {
                lock (writesSync)
                {
                    return writes.Count == 3;
                }
            },
            TimeSpan.FromSeconds(5));

        Assert.True(drained, "Expected the write pump to drain queued writes.");
        pump.RequestStop(discardPendingWrites: false);
        Assert.True(pump.Join(TimeSpan.FromSeconds(5)));

        lock (writesSync)
        {
            Assert.Equal("normal-1", System.Text.Encoding.UTF8.GetString(writes[0]));
            Assert.Equal(new byte[] { 0x0C }, writes[1]);
            Assert.Equal("normal-2", System.Text.Encoding.UTF8.GetString(writes[2]));
        }
    }

    [Fact]
    public async Task WritePump_WriteFailure_NotifiesCallerAndStopsFurtherWrites()
    {
        ManualResetEventSlim faultRaised = new(false);
        Exception? observed = null;

        using TransportWritePump pump = new(
            "TransportWritePumpTests.Fault",
            payload =>
            {
                _ = payload;
                throw new InvalidOperationException("boom");
            },
            exception =>
            {
                observed = exception;
                faultRaised.Set();
            });

        Assert.True(pump.TryEnqueue("boom"u8.ToArray()));
        Assert.True(faultRaised.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(observed);

        bool rejectedFurtherWrites = await WaitUntilAsync(
            () => !pump.TryEnqueue("after-fault"u8.ToArray()),
            TimeSpan.FromSeconds(2));

        Assert.True(rejectedFurtherWrites, "Expected the write pump to reject further writes after a failure.");
        pump.RequestStop(discardPendingWrites: true);
        Assert.True(pump.Join(TimeSpan.FromSeconds(5)));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return predicate();
    }
}
