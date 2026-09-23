// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalOutputWorkerTests
{
    [Fact]
    public void Schedule_IsSerialAndUsesOneDedicatedThread()
    {
        using ManualResetEventSlim firstEntered = new(initialState: false);
        using ManualResetEventSlim releaseFirst = new(initialState: false);
        List<int> threadIds = [];
        int invocation = 0;
        using TerminalOutputWorker worker = new(() =>
        {
            lock (threadIds)
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
            }

            if (Interlocked.Increment(ref invocation) == 1)
            {
                firstEntered.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
        });

        worker.Schedule();
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));
        for (int index = 0; index < 32; index++)
        {
            worker.Schedule();
        }

        releaseFirst.Set();
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref invocation) == 2,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(2, invocation);
        Assert.Single(threadIds.Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, threadIds[0]);
    }

    [Fact]
    public void Flush_WaitsForScheduledWork()
    {
        int value = 0;
        using TerminalOutputWorker worker = new(() => Volatile.Write(ref value, 42));

        worker.Schedule();
        worker.Flush();

        Assert.Equal(42, Volatile.Read(ref value));
    }

    [Fact]
    public void WorkerFailure_IsReportedByFlush()
    {
        using TerminalOutputWorker worker = new(() => throw new InvalidOperationException("parse failed"));
        worker.Schedule();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(worker.Flush);

        Assert.Equal("parse failed", exception.Message);
    }

    [Fact]
    public async Task Dispose_WaitsForActiveDrain_AndRejectsNewSchedules()
    {
        using ManualResetEventSlim entered = new(initialState: false);
        using ManualResetEventSlim release = new(initialState: false);
        using TerminalOutputWorker worker = new(() =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        worker.Schedule();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        Task firstDispose = Task.Run(worker.Dispose);
        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                try
                {
                    worker.Schedule();
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }, TimeSpan.FromSeconds(5)));
            Assert.False(firstDispose.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await firstDispose.WaitAsync(TimeSpan.FromSeconds(5));
        worker.Dispose();
        Assert.Throws<ObjectDisposedException>(worker.Schedule);
    }

    [Fact]
    public async Task Dispose_FromDrain_StillAllowsAnotherCallerToJoin()
    {
        using ManualResetEventSlim disposedFromDrain = new(initialState: false);
        using ManualResetEventSlim release = new(initialState: false);
        TerminalOutputWorker? worker = null;
        worker = new TerminalOutputWorker(() =>
        {
            worker!.Dispose();
            disposedFromDrain.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        });
        using (worker)
        {
            worker.Schedule();
            Assert.True(disposedFromDrain.Wait(TimeSpan.FromSeconds(5)));
            Task join = Task.Run(worker.Dispose);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                Assert.False(join.IsCompleted);
            }
            finally
            {
                release.Set();
            }

            await join.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void Flush_FromDrain_FailsWithoutDeadlocking()
    {
        TerminalOutputWorker? worker = null;
        worker = new TerminalOutputWorker(() => worker!.Flush());
        using (worker)
        {
            worker.Schedule();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(worker.Flush);
            Assert.Contains("own drain", exception.Message, StringComparison.Ordinal);
        }
    }
}
