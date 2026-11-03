// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SafePseudoConsoleHandleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Dispose_DefersNativeCloseUntilActiveOperationReturns()
    {
        using ManualResetEventSlim closed = new(false);
        int closeCount = 0;
        nint closedValue = 0;
        using SafePseudoConsoleHandle handle = new(123, value =>
        {
            closedValue = value;
            Interlocked.Increment(ref closeCount);
            closed.Set();
        });
        bool leaseHeld = false;
        handle.DangerousAddRef(ref leaseHeld);
        try
        {
            // The interop marshaller holds this same lease during ResizePseudoConsole.
            handle.Dispose();
            handle.Dispose();
            Assert.False(closed.IsSet);
            Assert.False(handle.IsClosed);
        }
        finally
        {
            if (leaseHeld) handle.DangerousRelease();
        }

        Assert.True(closed.Wait(Timeout));
        Assert.True(handle.IsClosed);
        Assert.Equal((nint)123, closedValue);
        Assert.Equal(1, Volatile.Read(ref closeCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalRelease_DoesNotWaitForOutputDrainOnCallingThread(bool releaseNativeLease)
    {
        using ManualResetEventSlim closeStarted = new(false);
        using ManualResetEventSlim outputDrained = new(false);
        using ManualResetEventSlim callerReturned = new(false);
        using ManualResetEventSlim closeFinished = new(false);
        Thread? closeThread = null;
        using SafePseudoConsoleHandle handle = new(123, _ =>
        {
            closeThread = Thread.CurrentThread;
            closeStarted.Set();
            outputDrained.Wait();
            closeFinished.Set();
        });
        bool leaseHeld = false;
        if (releaseNativeLease) handle.DangerousAddRef(ref leaseHeld);
        Thread reader = new(() =>
        {
            handle.Dispose();
            if (leaseHeld) handle.DangerousRelease();
            callerReturned.Set();
        }) { IsBackground = true };

        try
        {
            // Simulate pre-24H2 ClosePseudoConsole blocking until this caller
            // resumes draining output. Process.HasExited can raise Exited here,
            // or an in-flight native call can release the final lease here.
            // https://learn.microsoft.com/windows/console/closepseudoconsole#remarks
            reader.Start();
            Assert.True(closeStarted.Wait(Timeout));
            Assert.True(callerReturned.Wait(Timeout), "Final release blocked the output reader.");
            Assert.NotSame(reader, closeThread);
            Assert.True(closeThread!.IsBackground);
            Assert.False(closeThread.IsThreadPoolThread);
        }
        finally
        {
            outputDrained.Set();
            reader.Join(Timeout);
            closeFinished.Wait(Timeout);
            closeThread?.Join(Timeout);
        }
    }
}
