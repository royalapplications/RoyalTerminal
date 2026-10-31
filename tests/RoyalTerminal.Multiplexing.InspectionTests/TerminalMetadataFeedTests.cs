// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.InspectionTests;

public sealed class TerminalMetadataFeedTests
{
    [Fact]
    public async Task ObservesTransitionsWithinOneChunkAndPaginatesWithoutSkipping()
    {
        Wire wire = new();
        using InspectingTerminalTransport inspection = new(wire, new(80, 24, 0, 0));
        var baseline = await inspection.Metadata.ReadAsync();
        Assert.Empty(baseline.Events);
        var waiting = inspection.Metadata.ReadAsync(baseline.NextSequence, limit: 2, timeoutMs: 5000).AsTask();
        wire.Output("\a\u001b]2;build\u001b\\\u001b]7501;state=working:id=task\u001b\\\u001b]7501;state=blocked:id=task:kind=permission\u001b\\");
        var first = await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.InRange(first.Events.Count, 1, 2);
        List<TerminalMetadataEvent> events = [.. first.Events];
        wire.Exit(37);
        var page = first;
        while (!page.Ended)
        {
            page = await inspection.Metadata.ReadAsync(page.NextSequence, limit: 1);
            Assert.Single(page.Events); events.Add(page.Events[0]);
        }
        Assert.Equal(Enumerable.Range(1, events.Count).Select(i => (long)i), events.Select(e => e.Sequence));
        Assert.Equal("terminal.bell", events[0].Name);
        Assert.Contains(events, e => e.Name == "terminal.title_changed" && e.Text == "build");
        var transitions = events.Where(e => e.Name == "terminal.program_status_changed").ToArray();
        Assert.Equal(3, transitions.Length);
        Assert.Equal(TerminalProgramStatusState.Working, transitions[0].Record!.State);
        Assert.Equal(TerminalProgramStatusKind.Permission, transitions[1].Record!.Kind);
        Assert.Equal("removed", transitions[2].Reason);
        Assert.Equal(37, events[^1].ExitCode);
        Assert.Empty(wire.Input);
    }

    [Fact]
    public async Task EvictionIsAnExplicitGapAndSnapshotCursorNeverSkipsUndeliveredPages()
    {
        Wire wire = new(); using InspectingTerminalTransport inspection = new(wire, new(80, 24, 0, 0));
        wire.Output(new string('\a', 700));
        var now = await inspection.Metadata.ReadAsync();
        Assert.Empty(now.Events); Assert.Equal(700, now.NextSequence); Assert.True(now.FirstSequence > 1);
        var gap = await Assert.ThrowsAsync<MultiplexerException>(() => inspection.Metadata.ReadAsync(0).AsTask());
        Assert.Equal(MultiplexerErrorCode.HistoryGap, gap.Code);
        var page = await inspection.Metadata.ReadAsync(now.FirstSequence - 1, limit: 2);
        Assert.Equal(now.FirstSequence + 1, page.NextSequence); Assert.True(page.NextSequence < page.LastSequence);
        var ahead = await Assert.ThrowsAsync<MultiplexerException>(() => inspection.Metadata.ReadAsync(701).AsTask());
        Assert.Equal(MultiplexerErrorCode.Conflict, ahead.Code);
    }

    [Fact]
    public async Task OneCancelledObserverDoesNotCancelOtherReadersAndDisposeWakesIdleReader()
    {
        using CancellationTokenSource cts = new();
        Wire wire = new(); using InspectingTerminalTransport inspection = new(wire, new(80, 24, 0, 0));
        var cancelled = inspection.Metadata.ReadAsync(0, timeoutMs: 5000, cancellationToken: cts.Token).AsTask();
        var other = inspection.Metadata.ReadAsync(0, timeoutMs: 5000).AsTask();
        cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(other.IsCompleted);
        inspection.Dispose();
        var page = await other.WaitAsync(TimeSpan.FromSeconds(3)); Assert.True(page.Ended); Assert.Empty(page.Events);
    }

    private sealed class Wire : ITerminalTransport
    {
        public event Action<byte[],int>? DataReceived;
        public event Action<int>? ProcessExited;
        public bool IsRunning { get; private set; }
        public List<byte> Input { get; } = [];
        public void Output(string s) { byte[] bytes = Encoding.UTF8.GetBytes(s); DataReceived?.Invoke(bytes, bytes.Length); }
        public void Exit(int code) { IsRunning = false; ProcessExited?.Invoke(code); }
        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken token = default) { IsRunning = true; return ValueTask.CompletedTask; }
        public ValueTask StopAsync() { IsRunning = false; return ValueTask.CompletedTask; }
        public void SendInput(ReadOnlySpan<byte> bytes) => Input.AddRange(bytes.ToArray());
        public void Resize(TerminalSessionDimensions dimensions) { }
        public void Dispose() { IsRunning = false; }
    }
}
