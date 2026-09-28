// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Avalonia.Services;
using System.Runtime.InteropServices;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class UnixThreadSchedulingTests(ITestOutputHelper output)
{
    // Ghostty termio/Exec.zig sets user-initiated QoS on both gather/parse stages.
    // WT ConptyConnection owns and joins its output thread before releasing IO;
    // xterm.js InputHandler uses its host event loop, without an OS QoS contract.
    // Preserve owned/joined lifetime here; don't promote shared runtime threads.
    [Fact]
    public void OwnedThreadRetainsUserInitiatedQosInsideManagedWork()
    {
        int caller = Environment.CurrentManagedThreadId;
        ITerminalThread? thread = null;
        thread = UnixThreadScheduling.CreateUserInitiatedThread(() =>
        {
            Assert.True(thread!.IsCurrent);
            Assert.NotEqual(caller, Environment.CurrentManagedThreadId);
            Assert.True(Thread.CurrentThread.IsBackground);
            Assert.False(Thread.CurrentThread.IsThreadPoolThread);
            Assert.Equal("QoS test", Thread.CurrentThread.Name);
            if (OperatingSystem.IsMacOS())
            {
                Assert.Equal(0x19u, GetCurrentQosClass());
                Thread.Yield();
                Assert.Equal(0x19u, GetCurrentQosClass());
                Assert.True(UnixThreadScheduling.TrySetCurrentThreadUserInitiated(out int error));
                Assert.Equal(0, error);
            }
        }, "QoS test");
        Assert.False(thread.IsCurrent);
        thread.Start();
        thread.Join();
        Assert.False(thread.IsCurrent);
        thread.Join();
    }

    [Fact]
    public void ActualParseWorkerRetainsUserInitiatedQos()
    {
        if (!OperatingSystem.IsMacOS()) return;
        uint qos = 0;
        using TerminalOutputWorker worker = new(() => qos = GetCurrentQosClass());
        worker.Flush();
        Assert.Equal(0x19u, qos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExecutionContextIsCapturedAtStartAndChangesDoNotEscape(bool managedFallback)
    {
        AsyncLocal<string> local = new() { Value = "constructed" };
        ITerminalThread thread = CreateThread(() =>
        {
            Assert.Equal("started", local.Value);
            local.Value = "worker";
        }, "context", managedFallback);
        local.Value = "started";
        thread.Start();
        thread.Join();
        Assert.Equal("started", local.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedExecutionContextDoesNotFlowIntoWorker(bool managedFallback)
    {
        AsyncLocal<string> local = new() { Value = "not inherited" };
        ITerminalThread thread = CreateThread(
            () => Assert.Null(local.Value), "suppressed context", managedFallback);
        using (ExecutionContext.SuppressFlow()) thread.Start();
        thread.Join();
        Assert.Equal("not inherited", local.Value);
    }

    [Fact]
    public void WorkerRejectsInvalidStartJoinAndArguments()
    {
        Assert.Throws<ArgumentNullException>(() => UnixThreadScheduling.CreateUserInitiatedThread(null!, "name"));
        Assert.Throws<ArgumentNullException>(() => UnixThreadScheduling.CreateUserInitiatedThread(() => { }, null!));
        ITerminalThread thread = UnixThreadScheduling.CreateUserInitiatedThread(() => { }, "once");
        Assert.Throws<ThreadStateException>(thread.Join);
        thread.Start();
        thread.Join();
        Assert.Throws<ThreadStateException>(thread.Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerCannotJoinItselfAndFailuresAreObservedByEveryJoiner(bool managedFallback)
    {
        InvalidOperationException expected = new("owned worker failure");
        ITerminalThread? thread = null;
        thread = CreateThread(() =>
        {
            Assert.Throws<InvalidOperationException>(thread!.Join);
            throw expected;
        }, "failure", managedFallback);
        thread.Start();
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(thread.Join));
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(thread.Join));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentJoinersWaitForCallbackCompletion(bool managedFallback)
    {
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        ITerminalThread thread = CreateThread(() =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        }, "joining", managedFallback);
        thread.Start();
        Task[] joiners = new Task[4];
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            for (int i = 0; i < joiners.Length; i++) joiners[i] = Task.Run(thread.Join);
            await Task.Delay(50);
            foreach (Task joiner in joiners) Assert.False(joiner.IsCompleted);
        }
        finally { release.Set(); }
        await Task.WhenAll(joiners).WaitAsync(TimeSpan.FromSeconds(5));
        thread.Join();
    }

    [Fact]
    public void RepeatedWorkerCreationAndJoinReleasesNativeThreadResources()
    {
        int completed = 0;
        for (int i = 0; i < 128; i++)
        {
            ITerminalThread thread = UnixThreadScheduling.CreateUserInitiatedThread(
                () => Interlocked.Increment(ref completed), "lifecycle");
            thread.Start();
            thread.Join();
        }
        Assert.Equal(128, completed);
    }

    private static ITerminalThread CreateThread(Action action, string name, bool managedFallback)
        => managedFallback ? new ManagedTerminalThread(action, name)
            : UnixThreadScheduling.CreateUserInitiatedThread(action, name);

    [Fact]
    public void DedicatedThread_RequestsUserInitiatedQosOrPreservesExistingPolicy()
    {
        if (!OperatingSystem.IsMacOS()) return;
        bool applied = false;
        int error = 0;
        uint before = 0;
        uint after = 0;
        Thread thread = new(() =>
        {
            if (OperatingSystem.IsMacOS())
            {
                before = GetCurrentQosClass();
                applied = UnixThreadScheduling.TrySetCurrentThreadUserInitiated(out error);
                after = GetCurrentQosClass();
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        output.WriteLine($"QoS request applied={applied}, error={error}, before=0x{before:x}, after=0x{after:x}");
        if (applied)
        {
            Assert.Equal(0, error);
            Assert.Equal(0x19u, after);
        }
        else
        {
            Assert.NotEqual(0, error);
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public async Task SharedThreadPoolThread_DoesNotChangeQos()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.False(await Task.Run(UnixThreadScheduling.TrySetCurrentThreadUserInitiated));
    }

    [LibraryImport("libSystem.dylib", EntryPoint = "qos_class_self")]
    private static partial uint GetCurrentQosClass();
}
