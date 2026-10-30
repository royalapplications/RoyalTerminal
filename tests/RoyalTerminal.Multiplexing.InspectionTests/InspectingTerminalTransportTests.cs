// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.InspectionTests;

public sealed class InspectingTerminalTransportTests
{
    [Fact]
    public async Task CaptureUsesTerminalStateRatherThanStrippingEscapeSequences()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        inner.Emit("alpha\r\nbravo\u001b[1;1HZ");
        Assert.Equal("Zlpha\nbravo", transport.Capture(TerminalSnapshotExportFormat.PlainText));
        inner.Emit("\u001b[2J\u001b[H\u001b[31m<&>\u001b[0m");
        string html = transport.Capture(TerminalSnapshotExportFormat.Html);
        Assert.Contains("&lt;&amp;&gt;", html);
        string styled = transport.Capture(TerminalSnapshotExportFormat.StyledVt);
        Assert.Contains("\u001b[", styled);
        Assert.Contains("<&>", styled);
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.Capture((TerminalSnapshotExportFormat)99));
    }

    [Fact]
    public async Task AlternateScreenCaptureDoesNotLeakPrimaryHistory()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        inner.Emit("history one\r\nhistory two\r\nhistory three\r\nhistory four\r\nhistory five");
        string primary = transport.Capture(TerminalSnapshotExportFormat.PlainText);
        Assert.Contains("history one", primary);
        inner.Emit("\u001b[?1049h\u001b[Heditor");
        Assert.Equal("editor", transport.Capture(TerminalSnapshotExportFormat.PlainText));
        inner.Emit("\u001b[?1049l");
        Assert.Equal(primary, transport.Capture(TerminalSnapshotExportFormat.PlainText));
    }

    [Fact]
    public async Task MetadataAndProgramStatusSurviveFragmentedOutputAndSnapshotsAreOwned()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        byte[] output = Encoding.UTF8.GetBytes("\u001b]2;Build Ω\u001b\\\u001b]7;file://host/work/project\a" +
            "\u001b]7501;state=working:app=cargo:progress=20\u001b\\" +
            "\u001b]7501;state=blocked:id=test:kind=question:msg=Q29udGludWU/\a");
        foreach (byte item in output) inner.Emit([item]);
        Assert.Equal("Build Ω", transport.Title);
        Assert.Equal("file://host/work/project", transport.WorkingDirectory);
        IReadOnlyList<TerminalProgramStatus> before = transport.ProgramStatuses;
        Assert.Equal(2, before.Count);
        Assert.Equal("cargo", transport.GetProgramStatusApplication("test"));
        Assert.Equal("Continue?", before[1].Message);
        inner.Emit("\u001b]7501;state=done:app=cargo\a");
        Assert.Equal(TerminalProgramStatusState.Working, before[0].State);
        Assert.Equal(TerminalProgramStatusState.Done, transport.ProgramStatuses[1].State);
    }

    [Fact]
    public async Task ServerAnswersOnceWhileEveryObserverReceivesOriginalOutput()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        List<byte[]> first = [];
        List<byte[]> second = [];
        transport.DataReceived += (bytes, count) => first.Add(bytes.AsSpan(0, count).ToArray());
        transport.DataReceived += (bytes, count) => second.Add(bytes.AsSpan(0, count).ToArray());
        inner.Emit("abc\u001b[6n");
        Assert.Single(inner.Inputs);
        Assert.Equal("\u001b[1;4R", Encoding.UTF8.GetString(inner.Inputs[0]));
        Assert.Equal("abc\u001b[6n", Encoding.UTF8.GetString(first.Single()));
        Assert.Equal(first[0], second.Single());
        transport.SendInput("user input"u8);
        Assert.Equal("user input", Encoding.UTF8.GetString(inner.Inputs[1]));
    }

    [Fact]
    public async Task QueriesDuringStartupAreSentOnlyAfterTheUnderlyingTransportStarts()
    {
        FakeTransport inner = new() { OnStart = transport => transport.Emit("\u001b[5n\u001b]7501;?\a") };
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        Assert.False(inner.InputBeforeStart);
        Assert.Equal(2, inner.Inputs.Count);
        Assert.Equal("\u001b[0n", Encoding.UTF8.GetString(inner.Inputs[0]));
        Assert.Contains("states=idle,working,done,blocked,error", Encoding.UTF8.GetString(inner.Inputs[1]));
    }

    [Fact]
    public async Task NativeReplyDeliveryDoesNotHoldTheParserLock()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        inner.OnInput = () =>
        {
            Task callback = Task.Run(() => inner.Emit("response arrived"));
            Assert.True(callback.Wait(TimeSpan.FromSeconds(5)), "The transport response blocked a cross-thread output callback.");
        };
        inner.Emit("\u001b[5n");
        Assert.Contains("response arrived", transport.Capture(TerminalSnapshotExportFormat.PlainText));
        Assert.Null(transport.ResponseError);
    }

    [Fact]
    public async Task ResizeCommitsCanonicalGeometryBeforeSynchronousNativeRedraw()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        TerminalSessionDimensions resized = new(40, 8, 400, 160);
        inner.OnResize = dimensions =>
        {
            Assert.Equal(resized, transport.Dimensions);
            Task callback = Task.Run(() => inner.Emit("\u001b[8;40H!\u001b[6n"));
            Assert.True(callback.Wait(TimeSpan.FromSeconds(5)), "Resize held the parser lock during a transport callback.");
        };
        transport.Resize(resized);
        Assert.Equal(resized, transport.Dimensions);
        Assert.Contains("\u001b[8;40R", inner.Inputs.Select(Encoding.UTF8.GetString));
        Assert.Contains("!", transport.Capture(TerminalSnapshotExportFormat.PlainText));
    }

    [Fact]
    public async Task RejectedResizeRestoresGeometryAndInvalidDimensionsNeverReachTransport()
    {
        FakeTransport inner = new() { OnResize = _ => throw new IOException("resize rejected") };
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        Assert.Throws<IOException>(() => transport.Resize(new(40, 8, 0, 0)));
        Assert.Equal(Dimensions, transport.Dimensions);
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.Resize(new(4096, 4096, 0, 0)));
        Assert.Equal(1, inner.ResizeCount);
    }

    [Fact]
    public async Task ConcurrentOutputHasOneOrderForCanonicalScreenAndSubscribers()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, new(80, 4, 0, 0));
        await transport.StartAsync(new Options(new(80, 4, 0, 0)));
        StringBuilder forwarded = new();
        transport.DataReceived += (bytes, count) => forwarded.Append(Encoding.UTF8.GetString(bytes, 0, count));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => inner.Emit($"row {i}\r\n"))));
        Assert.Equal(forwarded.ToString().Replace("\r", string.Empty).TrimEnd('\n'),
            transport.Capture(TerminalSnapshotExportFormat.PlainText));
    }

    [Fact]
    public async Task ProcessExitCleansTransientStatusBeforePublishingExitAndPreservesCapture()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        inner.Emit("final output\u001b]7501;state=working:id=work\a\u001b]7501;state=done:id=done\a");
        int exits = 0;
        transport.ProcessExited += code =>
        {
            exits++;
            Assert.Equal(7, code);
            Assert.Equal(TerminalProgramStatusState.Done, Assert.Single(transport.ProgramStatuses).State);
            Assert.Equal("final output", transport.Capture(TerminalSnapshotExportFormat.PlainText));
        };
        inner.Exit(7);
        inner.Exit(7);
        inner.Emit("invalid late bytes");
        Assert.Equal(1, exits);
        Assert.Equal("final output", transport.Capture(TerminalSnapshotExportFormat.PlainText));
    }

    [Fact]
    public async Task ReplyFailureIsInspectableAndDoesNotDiscardTerminalOutput()
    {
        FakeTransport inner = new() { OnInput = () => throw new IOException("input closed") };
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        await transport.StartAsync(new Options(Dimensions));
        inner.Emit("before\u001b[5nafter");
        Assert.IsType<IOException>(transport.ResponseError);
        Assert.Equal("beforeafter", transport.Capture(TerminalSnapshotExportFormat.PlainText));
        inner.Emit("\u001b[5n");
        Assert.Single(inner.Inputs);
    }

    [Fact]
    public async Task ReplyFloodIsBoundedAndHostEffectsRemainUnavailable()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions, new() { MaxPendingResponseBytes = 5 });
        await transport.StartAsync(new Options(Dimensions));
        inner.Emit("\u001b]52;c;c2VjcmV0\a\u001b]52;c;?\a\u001b[21t\u001b[5n\u001b[5ntext");
        Assert.IsType<IOException>(transport.ResponseError);
        Assert.Empty(inner.Inputs);
        Assert.Equal("text", transport.Capture(TerminalSnapshotExportFormat.PlainText));
    }

    [Fact]
    public async Task FactoryUsesReferenceIdentityAndUnregistersDisposedTransports()
    {
        FakeFactory inner = new();
        InspectingTerminalTransportFactory factory = new(inner);
        Options first = new(Dimensions);
        Options equal = new(Dimensions);
        Assert.Equal(first, equal);
        using ITerminalTransport firstTransport = factory.Create(first);
        using ITerminalTransport secondTransport = factory.Create(equal);
        Assert.True(factory.TryGetInspection(first, out InspectingTerminalTransport? firstInspection));
        Assert.True(factory.TryGetInspection(equal, out InspectingTerminalTransport? secondInspection));
        Assert.NotSame(firstInspection, secondInspection);
        Assert.False(factory.TryGetInspection(new Options(Dimensions), out _));
        Assert.Throws<InvalidOperationException>(() => factory.Create(first));
        await firstTransport.StartAsync(first);
        firstTransport.Dispose();
        firstTransport.Dispose();
        Assert.False(factory.TryGetInspection(first, out _));
        Assert.True(factory.TryGetInspection(equal, out _));
        Assert.Equal(1, inner.Transports[0].DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => firstInspection.Title);
        using ITerminalTransport replacement = factory.Create(first);
        Assert.True(factory.TryGetInspection(first, out _));
    }

    [Fact]
    public async Task BrokerKeepsCanonicalInspectionWhileAllClientsAreDetached()
    {
        FakeFactory inner = new();
        InspectingTerminalTransportFactory factory = new(inner);
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("inspect");
        Options options = new(Dimensions);
        MultiplexerBlockDescriptor block = await broker.AddBlockAsync(session.Id, "terminal", options);
        Assert.True(factory.TryGetInspection(options, out InspectingTerminalTransport? inspection));
        using (MultiplexerAttachment client = broker.Attach(session.Id, block.Id))
            inner.Transports[0].Emit("before\r\n");
        inner.Transports[0].Emit("detached\u001b[5n");
        Assert.Equal("before\ndetached", inspection.Capture(TerminalSnapshotExportFormat.PlainText));
        Assert.Single(inner.Transports[0].Inputs);
        await broker.CloseBlockAsync(session.Id, block.Id);
        Assert.False(factory.TryGetInspection(options, out _));
    }

    [Fact]
    public async Task LifecycleRejectsRepeatedStartAndStopKeepsLastScreenInspectable()
    {
        FakeTransport inner = new();
        using InspectingTerminalTransport transport = new(inner, Dimensions);
        Options options = new(Dimensions);
        await transport.StartAsync(options);
        inner.Emit("kept");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await transport.StartAsync(options));
        await transport.StopAsync();
        Assert.False(transport.IsRunning);
        Assert.Equal("kept", transport.Capture(TerminalSnapshotExportFormat.PlainText));
    }

    private static TerminalSessionDimensions Dimensions => new(20, 4, 200, 80);

    private sealed record Options(TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
    {
        public string TransportId => "fake";
    }

    private sealed class FakeFactory : ITerminalTransportFactory
    {
        internal List<FakeTransport> Transports { get; } = [];
        public ITerminalTransport Create(ITerminalTransportOptions options)
        {
            FakeTransport transport = new();
            Transports.Add(transport);
            return transport;
        }
    }

    private sealed class FakeTransport : ITerminalTransport
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;
        public bool IsRunning { get; private set; }
        internal List<byte[]> Inputs { get; } = [];
        internal Action<FakeTransport>? OnStart { get; init; }
        internal Action<TerminalSessionDimensions>? OnResize { get; set; }
        internal Action? OnInput { get; set; }
        internal bool InputBeforeStart { get; private set; }
        internal int ResizeCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnStart?.Invoke(this);
            IsRunning = true;
            return ValueTask.CompletedTask;
        }

        public void SendInput(ReadOnlySpan<byte> utf8)
        {
            InputBeforeStart |= !IsRunning;
            lock (Inputs) Inputs.Add(utf8.ToArray());
            OnInput?.Invoke();
        }

        public void Resize(TerminalSessionDimensions dimensions)
        {
            ResizeCount++;
            OnResize?.Invoke(dimensions);
        }

        public ValueTask StopAsync() { Exit(0); return ValueTask.CompletedTask; }
        public void Dispose() { DisposeCount++; IsRunning = false; }
        internal void Emit(string text) => Emit(Encoding.UTF8.GetBytes(text));
        internal void Emit(byte[] bytes) => DataReceived?.Invoke(bytes, bytes.Length);
        internal void Exit(int code) { IsRunning = false; ProcessExited?.Invoke(code); }
    }
}
