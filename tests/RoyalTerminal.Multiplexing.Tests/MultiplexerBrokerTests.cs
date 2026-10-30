// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class MultiplexerBrokerTests
{
    [Fact]
    public async Task DetachPreservesProcessAndReattachReplaysOutputAndDimensions()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("work");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using (MultiplexerAttachment first = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller))
        {
            first.SendInput("echo hello\r"u8);
            factory.Last.Emit("hello\r\n");
            Assert.Equal("echo hello\r", Encoding.UTF8.GetString(factory.Last.Inputs.Single()));
        }

        Assert.True(factory.Last.IsRunning);
        Assert.Equal(0, factory.Last.StopCount);
        factory.Last.Emit("while detached\r\n");
        using MultiplexerAttachment second = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        MultiplexerEvent[] events = Drain(second);
        Assert.Equal([1L, 2L, 3L], events.Select(item => item.Sequence).ToArray());
        Assert.Equal(MultiplexerEventKind.Resize, events[0].Kind);
        Assert.Equal("hello\r\nwhile detached\r\n", Output(events));
    }

    [Fact]
    public async Task ObserverCannotSendInputResizeOrTakeControllerAuthority()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("roles");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment observer = broker.Attach(session.Id, block.Id);
        using MultiplexerAttachment controller = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Assert.Equal(MultiplexerErrorCode.Forbidden, Assert.Throws<MultiplexerException>(() => observer.SendInput("x"u8)).Code);
        Assert.Equal(MultiplexerErrorCode.Forbidden, Assert.Throws<MultiplexerException>(() => observer.Resize(new(100, 30, 1000, 600))).Code);
        Assert.Equal(MultiplexerErrorCode.Conflict,
            Assert.Throws<MultiplexerException>(() => broker.Attach(session.Id, block.Id, MultiplexerRole.Controller)).Code);
        controller.Dispose();
        Assert.False(observer.HasControl);
        using MultiplexerAttachment replacement = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Assert.True(replacement.HasControl);
    }

    [Fact]
    public async Task ReplayCursorDoesNotRepeatAppliedEvents()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("cursor");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        factory.Last.Emit("one");
        factory.Last.Emit("two");
        using MultiplexerAttachment attachment = broker.Attach(session.Id, block.Id, lastSequence: 2);
        Assert.Equal("two", Output(Drain(attachment)));
        factory.Last.Emit("three");
        Assert.Equal(4, (await attachment.Events.ReadAsync()).Sequence);
        Assert.Equal(MultiplexerErrorCode.Conflict,
            Assert.Throws<MultiplexerException>(() => broker.Attach(session.Id, block.Id, lastSequence: 5)).Code);
    }

    [Fact]
    public async Task LostHistoryIsExplicitAndNeverPresentedAsCompleteState()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory, new() { MaxJournalBytes = 4, MaxEventBytes = 4, MaxJournalEvents = 2 });
        MultiplexerSessionDescriptor session = broker.CreateSession("gap");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        factory.Last.Emit("abcd");
        factory.Last.Emit("efgh");
        Assert.Equal(MultiplexerErrorCode.HistoryGap,
            Assert.Throws<MultiplexerException>(() => broker.Attach(session.Id, block.Id)).Code);
        using MultiplexerAttachment resumed = broker.Attach(session.Id, block.Id, lastSequence: 2);
        Assert.Equal("efgh", Output(Drain(resumed)));
        Assert.Equal(3, broker.GetSession(session.Id).Blocks[0].FirstRetainedSequence);
    }

    [Fact]
    public async Task SlowObserverIsDisconnectedWithoutBlockingFastObserverOrProcess()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory, new() { MaxPendingEventsPerAttachment = 2 });
        MultiplexerSessionDescriptor session = broker.CreateSession("backpressure");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment slow = broker.Attach(session.Id, block.Id);
        using MultiplexerAttachment fast = broker.Attach(session.Id, block.Id);
        Drain(fast);
        factory.Last.Emit("first");
        Assert.Equal("first", Output(Drain(fast)));
        factory.Last.Emit("second");
        Assert.False(slow.IsConnected);
        Assert.True(fast.IsConnected);
        Assert.True(factory.Last.IsRunning);
        Assert.Equal("second", Output(Drain(fast)));
        Drain(slow);
        MultiplexerException failure = await Assert.ThrowsAsync<MultiplexerException>(() => slow.Events.Completion);
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded, failure.Code);
    }

    [Fact]
    public async Task PendingByteBudgetIsReleasedWhenReaderConsumesEvents()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory, new() { MaxPendingBytesPerAttachment = 4 });
        MultiplexerSessionDescriptor session = broker.CreateSession("bytes");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment reader = broker.Attach(session.Id, block.Id);
        factory.Last.Emit("abcd");
        Assert.Equal("abcd", Output(Drain(reader)));
        factory.Last.Emit("efgh");
        Assert.True(reader.IsConnected);
        factory.Last.Emit("i");
        Assert.False(reader.IsConnected);
    }

    [Fact]
    public async Task RecipientMutationAndTransportBufferReuseCannotCorruptHistory()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("ownership");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment first = broker.Attach(session.Id, block.Id);
        byte[] output = "unchanged"u8.ToArray();
        factory.Last.Emit(output);
        output[0] = (byte)'X';
        MultiplexerEvent item = Drain(first).Single(item => item.Kind == MultiplexerEventKind.Output);
        item.Data![1] = (byte)'Y';
        using MultiplexerAttachment second = broker.Attach(session.Id, block.Id);
        Assert.Equal("unchanged", Output(Drain(second)));
    }

    [Fact]
    public async Task ResizePrecedesSynchronousTransportRedrawAndPreservesPixelOnlyChanges()
    {
        FakeFactory factory = new() { Configure = transport => transport.OnResize = () => transport.Emit("redraw") };
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("resize");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment controller = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Drain(controller);
        TerminalSessionDimensions resized = new(80, 24, 1600, 960);
        controller.Resize(resized);
        MultiplexerEvent[] events = Drain(controller);
        Assert.Equal([MultiplexerEventKind.Resize, MultiplexerEventKind.Output], events.Select(item => item.Kind).ToArray());
        Assert.Equal(resized, events[0].Dimensions);
        Assert.Equal(resized, broker.GetSession(session.Id).Blocks[0].Dimensions);
        controller.Resize(resized);
        Assert.Empty(Drain(controller));
    }

    [Fact]
    public async Task InvalidResizeDoesNotReachTransportOrMutateState()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("invalid");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment controller = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Drain(controller);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Resize(new(0, 24, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Resize(new(80, 24, -1, 0)));
        Assert.Empty(factory.Last.Resizes);
        Assert.Empty(Drain(controller));
        Assert.Equal(block.Dimensions, broker.GetSession(session.Id).Blocks[0].Dimensions);
    }

    [Fact]
    public async Task FailedResizePublishesRollbackInStreamOrder()
    {
        FakeFactory factory = new() { Configure = transport => transport.OnResize = () => throw new IOException("resize failed") };
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("rollback");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment controller = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Drain(controller);
        Assert.Throws<IOException>(() => controller.Resize(new(100, 30, 1000, 600)));
        MultiplexerEvent[] events = Drain(controller);
        Assert.Equal(2, events.Length);
        Assert.Equal(block.Dimensions, events[1].Dimensions);
        Assert.Equal(block.Dimensions, broker.GetSession(session.Id).Blocks[0].Dimensions);
    }

    [Fact]
    public async Task ExitedBlockRetainsOutputAndExitForLateAttachment()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("exit");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        factory.Last.Emit("final output");
        factory.Last.Exit(42);
        factory.Last.Exit(99);
        using MultiplexerAttachment late = broker.Attach(session.Id, block.Id);
        MultiplexerEvent[] events = Drain(late);
        Assert.Equal("final output", Output(events));
        Assert.Equal(42, Assert.Single(events, item => item.Kind == MultiplexerEventKind.Exit).ExitCode);
        await late.Events.Completion;
        Assert.False(late.IsConnected);
        Assert.Equal(MultiplexerBlockState.Exited, broker.GetSession(session.Id).Blocks[0].State);
    }

    [Fact]
    public async Task ImmediateExitDuringStartupIsNotOverwrittenByRunningState()
    {
        FakeFactory factory = new() { Configure = transport => transport.OnStart = () => transport.Exit(0) };
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("immediate");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        Assert.Equal(MultiplexerBlockState.Exited, block.State);
        Assert.Equal(0, block.ExitCode);
    }

    [Fact]
    public async Task FailedStartupRollsBackLayoutAndDisposesOwnedTransport()
    {
        FakeFactory factory = new() { Configure = transport => transport.OnStart = () => throw new IOException("start failed") };
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("failure");
        await Assert.ThrowsAsync<IOException>(() => broker.AddBlockAsync(session.Id, "shell", Options()).AsTask());
        MultiplexerSessionDescriptor restored = broker.GetSession(session.Id);
        Assert.Empty(restored.Blocks);
        Assert.Null(restored.Windows[0].Layout);
        Assert.Equal(1, factory.Last.StopCount);
        Assert.Equal(1, factory.Last.DisposeCount);
    }

    [Fact]
    public async Task SessionCloseCancelsStartingTransportAndCleansItExactlyOnce()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeFactory factory = new() { Configure = transport => { transport.StartWait = release.Task; transport.OnStart = () => entered.SetResult(); } };
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("cancel");
        Task<MultiplexerBlockDescriptor> starting = broker.AddBlockAsync(session.Id, "shell", Options()).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await broker.CloseSessionAsync(session.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Equal(1, factory.Last.StopCount);
        Assert.Equal(1, factory.Last.DisposeCount);
        Assert.Empty(broker.ListSessions());
    }

    [Fact]
    public async Task AttachRacingOutputDoesNotLoseOrDuplicateEvents()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("race");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        Task producer = Task.Run(() => { for (int i = 0; i < 500; i++) factory.Last.Emit(i + "\n"); });
        using MultiplexerAttachment attachment = broker.Attach(session.Id, block.Id);
        long replayFence = attachment.ReplayThroughSequence;
        await producer;
        MultiplexerEvent[] events = Drain(attachment);
        Assert.Equal(501, events.Length);
        Assert.Equal(Enumerable.Range(1, 501).Select(value => (long)value), events.Select(item => item.Sequence));
        Assert.Equal(string.Concat(Enumerable.Range(0, 500).Select(value => value + "\n")), Output(events));
        Assert.InRange(replayFence, 1, 501);
        Assert.Equal(replayFence, attachment.ReplayThroughSequence);
        Assert.Equal(replayFence, events.LongCount(item => item.Sequence <= replayFence));
    }

    [Fact]
    public async Task ReplayFenceDoesNotMoveWhenOutputArrivesAfterAttach()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("fence");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        factory.Last.Emit("before");
        using MultiplexerAttachment attachment = broker.Attach(session.Id, block.Id);
        Assert.Equal(2, attachment.ReplayThroughSequence);
        factory.Last.Emit("after");
        Assert.Equal(3, broker.GetSession(session.Id).Blocks[0].LastSequence);
        Assert.Equal(2, attachment.ReplayThroughSequence);
        MultiplexerEvent[] events = Drain(attachment);
        Assert.Equal("before", Output(events.Where(item => item.Sequence <= attachment.ReplayThroughSequence)));
        Assert.Equal("after", Output(events.Where(item => item.Sequence > attachment.ReplayThroughSequence)));
    }

    [Fact]
    public async Task LargeOutputIsSplitWithoutLosingBytes()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory, new() { MaxEventBytes = 3 });
        MultiplexerSessionDescriptor session = broker.CreateSession("chunks");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        factory.Last.Emit("ab😀xyz");
        using MultiplexerAttachment attachment = broker.Attach(session.Id, block.Id);
        MultiplexerEvent[] events = Drain(attachment);
        Assert.All(events.Where(item => item.Data is not null), item => Assert.InRange(item.Data!.Length, 1, 3));
        Assert.Equal("ab😀xyz", Output(events));
    }

    [Fact]
    public async Task InputBudgetAndDisposedAttachmentsAreEnforced()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory, new() { MaxInputBytes = 3 });
        MultiplexerSessionDescriptor session = broker.CreateSession("input");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        MultiplexerAttachment attachment = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded, Assert.Throws<MultiplexerException>(() => attachment.SendInput("four"u8)).Code);
        Assert.Empty(factory.Last.Inputs);
        attachment.Dispose();
        Assert.Equal(MultiplexerErrorCode.Disconnected, Assert.Throws<MultiplexerException>(() => attachment.SendInput("x"u8)).Code);
    }

    [Fact]
    public async Task QuotasIncludeExitedBlocksAndRejectBeforeCreatingTransport()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory, new() { MaxSessions = 1, MaxBlocksPerSession = 1, MaxAttachmentsPerBlock = 1 });
        MultiplexerSessionDescriptor session = broker.CreateSession("quotas");
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded, Assert.Throws<MultiplexerException>(() => broker.CreateSession("extra")).Code);
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment attachment = broker.Attach(session.Id, block.Id);
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded, Assert.Throws<MultiplexerException>(() => broker.Attach(session.Id, block.Id)).Code);
        factory.Last.Exit(0);
        MultiplexerException error = await Assert.ThrowsAsync<MultiplexerException>(() => broker.AddBlockAsync(session.Id, "extra", Options()).AsTask());
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded, error.Code);
        Assert.Single(factory.Transports);
    }

    [Fact]
    public async Task DisposeWaitsForAllBlocksAndRepeatedCallsDoNotStopTwice()
    {
        FakeFactory factory = new();
        MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("dispose");
        await broker.AddBlockAsync(session.Id, "one", Options());
        await broker.AddBlockAsync(session.Id, "two", Options());
        await Task.WhenAll(broker.DisposeAsync().AsTask(), broker.DisposeAsync().AsTask());
        Assert.All(factory.Transports, transport => { Assert.Equal(1, transport.StopCount); Assert.Equal(1, transport.DisposeCount); });
        Assert.Throws<ObjectDisposedException>(() => broker.ListSessions());
    }

    [Fact]
    public async Task TransportMayPublishFromAnotherThreadWhileHandlingInputOrResize()
    {
        FakeFactory factory = new()
        {
            Configure = transport =>
            {
                transport.OnInput = () =>
                {
                    if (!Task.Run(() => transport.Emit("input response")).Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The input callback was blocked by the broker event gate.");
                };
                transport.OnResize = () =>
                {
                    if (!Task.Run(() => transport.Emit("resize response")).Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The resize callback was blocked by the broker event gate.");
                };
            },
        };
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("callbacks");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment controller = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Drain(controller);
        controller.SendInput("request"u8);
        controller.Resize(new(100, 40, 1000, 800));
        Assert.Equal("input responseresize response", Output(Drain(controller)));
    }

    [Fact]
    public async Task BrokerDisposalWaitsForAnExplicitCloseAlreadyInProgress()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeFactory factory = new() { Configure = transport => { transport.StopWait = release.Task; transport.OnStop = () => entered.SetResult(); } };
        MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("closing");
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "shell", Options());
        using MultiplexerAttachment controller = broker.Attach(session.Id, block.Id, MultiplexerRole.Controller);
        Task close = broker.CloseBlockAsync(session.Id, block.Id).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MultiplexerErrorCode.Disconnected,
            Assert.Throws<MultiplexerException>(() => controller.SendInput("during close"u8)).Code);
        Task dispose = broker.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        release.SetResult();
        await Task.WhenAll(close, dispose);
        Assert.Equal(1, factory.Last.StopCount);
        Assert.Equal(1, factory.Last.DisposeCount);
    }

    [Fact]
    public async Task StopFailureStillDisposesEveryOwnedBlock()
    {
        FakeFactory factory = new() { Configure = transport => transport.OnStop = () => throw new IOException("stop failed") };
        MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("stop failure");
        await broker.AddBlockAsync(session.Id, "one", Options());
        await broker.AddBlockAsync(session.Id, "two", Options());
        await Assert.ThrowsAsync<IOException>(() => broker.DisposeAsync().AsTask());
        Assert.All(factory.Transports, transport => Assert.Equal(1, transport.DisposeCount));
    }

    internal static PipeTransportOptions Options() => new(new("test", []), null, null, true, new(80, 24, 800, 480));

    internal static MultiplexerEvent[] Drain(MultiplexerAttachment attachment)
    {
        List<MultiplexerEvent> events = [];
        while (attachment.Events.TryRead(out MultiplexerEvent? item)) events.Add(item);
        return [.. events];
    }

    internal static string Output(IEnumerable<MultiplexerEvent> events)
        => Encoding.UTF8.GetString(events.Where(item => item.Kind == MultiplexerEventKind.Output).SelectMany(item => item.Data!).ToArray());
}

internal sealed class FakeFactory : ITerminalTransportFactory
{
    internal Action<FakeTransport>? Configure { get; init; }
    internal List<FakeTransport> Transports { get; } = [];
    internal FakeTransport Last => Transports[^1];

    public ITerminalTransport Create(ITerminalTransportOptions options)
    {
        FakeTransport transport = new();
        Configure?.Invoke(transport);
        Transports.Add(transport);
        return transport;
    }
}

internal sealed class FakeTransport : ITerminalTransport
{
    public event Action<byte[], int>? DataReceived;
    public event Action<int>? ProcessExited;
    public bool IsRunning { get; private set; }
    internal int StopCount { get; private set; }
    internal int DisposeCount { get; private set; }
    internal List<byte[]> Inputs { get; } = [];
    internal List<TerminalSessionDimensions> Resizes { get; } = [];
    internal Action? OnStart { get; set; }
    internal Action? OnInput { get; set; }
    internal Action? OnResize { get; set; }
    internal Action? OnStop { get; set; }
    internal Task? StartWait { get; set; }
    internal Task? StopWait { get; set; }

    public async ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = true;
        OnStart?.Invoke();
        if (StartWait is { } wait) await wait.WaitAsync(cancellationToken);
    }

    public void SendInput(ReadOnlySpan<byte> utf8) { Inputs.Add(utf8.ToArray()); OnInput?.Invoke(); }
    public void Resize(TerminalSessionDimensions dimensions) { Resizes.Add(dimensions); OnResize?.Invoke(); }
    public async ValueTask StopAsync()
    {
        StopCount++;
        OnStop?.Invoke();
        if (StopWait is { } wait) await wait;
        IsRunning = false;
    }
    public void Dispose() => DisposeCount++;
    internal void Emit(string value) => Emit(Encoding.UTF8.GetBytes(value));
    internal void Emit(byte[] value) => DataReceived?.Invoke(value, value.Length);
    internal void Exit(int code) { IsRunning = false; ProcessExited?.Invoke(code); }
}
