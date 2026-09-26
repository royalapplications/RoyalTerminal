// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalStateSynchronizationTests
{
    [Fact]
    public void AcquireDemand_UsesExistingScreenMonitor()
    {
        TerminalScreen screen = new(80, 24);
        Assert.Same(screen.SyncRoot, screen.Synchronization.SyncRoot);

        using (screen.Synchronization.AcquireDemand())
        {
            Assert.True(Monitor.IsEntered(screen.SyncRoot));
            Assert.Equal(0, screen.Synchronization.PendingDemand);
        }

        Assert.False(Monitor.IsEntered(screen.SyncRoot));
    }

    [Fact]
    public async Task AcquireDemand_PublishesWaitingRenderer_AndReleasesAfterHandoff()
    {
        TerminalStateSynchronization synchronization = new();
        bool rendered = false;
        Task render;
        lock (synchronization.SyncRoot)
        {
            render = Task.Run(() =>
            {
                using (synchronization.AcquireDemand())
                {
                    Assert.True(Monitor.IsEntered(synchronization.SyncRoot));
                    rendered = true;
                }
            });

            Assert.True(SpinWait.SpinUntil(
                () => synchronization.PendingDemand == 1,
                TimeSpan.FromSeconds(5)));
            Assert.False(rendered);
        }

        synchronization.YieldToDemand();
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(rendered);
        Assert.Equal(0, synchronization.PendingDemand);
    }

    [Fact]
    public void YieldToDemand_WithoutRenderer_DoesNotAllocate()
    {
        TerminalStateSynchronization synchronization = new();
        synchronization.YieldToDemand();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            synchronization.YieldToDemand();
        }

        Assert.Equal(allocatedBefore, GC.GetAllocatedBytesForCurrentThread());
    }
}
