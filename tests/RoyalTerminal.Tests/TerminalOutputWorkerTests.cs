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
        worker.Flush();

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
}
