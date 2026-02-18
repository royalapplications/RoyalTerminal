// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSessionServiceTransportTests
{
    [Fact]
    public async Task StartSessionAsync_ConfiguresVtCallbacksBeforeEarlyData()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new()
        {
            EmitDataDuringStart = "\x1B[6n"u8.ToArray(),
        };
        FixedTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();

        bool callbacksConfigured = false;
        Action<byte[], int> onData = (_, _) =>
        {
            callbacksConfigured =
                vtProcessor.ResponseCallback is not null
                && vtProcessor.BellCallback is not null
                && vtProcessor.TitleCallback is not null;
        };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        Assert.True(callbacksConfigured);
        Assert.Same(transport, service.Transport);
        Assert.True(service.HasActiveTransport);

        await service.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task StartSessionAsync_WithVtProcessor_ExposesModeSourceAndForwardsModeChanges()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new();
        FixedTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        ITerminalModeSource? modeSource = service.ModeSource;
        Assert.NotNull(modeSource);
        Assert.Equal(vtProcessor.ModeState, modeSource.ModeState);

        TerminalModeState? observed = null;
        modeSource.ModeChanged += (_, state) => observed = state;

        TerminalModeState updated = vtProcessor.ModeState with
        {
            ApplicationCursorKeys = true,
            BracketedPaste = true,
        };
        vtProcessor.SetModeState(updated);

        Assert.Equal(updated, observed);

        await service.StopSessionAsync(vtProcessor, onData, onExit);
        Assert.Null(service.ModeSource);

        observed = null;
        vtProcessor.SetModeState(updated with { BracketedPaste = false });
        Assert.Null(observed);
    }

    [Fact]
    public async Task StartSessionAsync_WhenStartFails_CleansCallbacksAndHandlers()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new()
        {
            ThrowOnStart = true,
        };
        FixedTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartSessionAsync(
                factory,
                new FakeTransportOptions(TerminalTransportIds.Pipe),
                vtProcessor,
                onData,
                onExit,
                _ => { },
                () => { },
                _ => { }));

        Assert.Null(vtProcessor.ResponseCallback);
        Assert.Null(vtProcessor.BellCallback);
        Assert.Null(vtProcessor.TitleCallback);
        Assert.Equal(0, transport.DataSubscriberCount);
        Assert.Equal(0, transport.ExitSubscriberCount);
        Assert.Null(service.Transport);
        Assert.False(service.HasActiveTransport);
        Assert.Null(service.ModeSource);
        Assert.True(transport.DisposeCalled);
    }

    [Fact]
    public async Task StartSessionAsync_WhenFactoryCreateFails_LeavesVtCallbacksUnset()
    {
        TerminalSessionService service = new();
        ThrowingFactory factory = new();
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartSessionAsync(
                factory,
                new FakeTransportOptions(TerminalTransportIds.Ssh),
                vtProcessor,
                onData,
                onExit,
                _ => { },
                () => { },
                _ => { }));

        Assert.Null(vtProcessor.ResponseCallback);
        Assert.Null(vtProcessor.BellCallback);
        Assert.Null(vtProcessor.TitleCallback);
        Assert.False(service.HasActiveTransport);
        Assert.Null(service.Transport);
    }

    [Fact]
    public async Task StopSessionAsync_UnwiresTransportAndClearsState()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new();
        FixedTransportFactory factory = new(transport);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        Assert.True(service.HasActiveTransport);
        Assert.Same(transport, service.Transport);
        Assert.Equal(1, transport.DataSubscriberCount);
        Assert.Equal(1, transport.ExitSubscriberCount);

        await service.StopSessionAsync(vtProcessor: null, onData, onExit);

        Assert.False(service.HasActiveTransport);
        Assert.Null(service.Transport);
        Assert.True(transport.StopCalled);
        Assert.True(transport.DisposeCalled);
        Assert.Equal(0, transport.DataSubscriberCount);
        Assert.Equal(0, transport.ExitSubscriberCount);
    }

    [Fact]
    public async Task StartSessionAsync_WhenSessionAlreadyActive_ThrowsAndKeepsExistingTransport()
    {
        TerminalSessionService service = new();
        FakeTransport firstTransport = new();
        FakeTransport secondTransport = new();
        FixedTransportFactory firstFactory = new(firstTransport);
        FixedTransportFactory secondFactory = new(secondTransport);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            firstFactory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartSessionAsync(
                secondFactory,
                new FakeTransportOptions(TerminalTransportIds.Ssh),
                vtProcessor: null,
                onData,
                onExit,
                _ => { },
                () => { },
                _ => { }));

        Assert.Contains("already active", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(firstTransport, service.Transport);
        Assert.False(secondTransport.DisposeCalled);

        await service.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task SendInput_WithActiveTransport_WritesUtf8Bytes()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new();
        FixedTransportFactory factory = new(transport);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        service.SendInput("hello");

        Assert.NotNull(transport.LastInputBytes);
        Assert.Equal("hello", Encoding.UTF8.GetString(transport.LastInputBytes));

        await service.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task StartSessionAsync_VtResponseCallback_RoundTripsToActiveTransport()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new();
        FixedTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onVtResponse = bytes => service.SendInput(bytes);

        await service.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            onVtResponse,
            () => { },
            _ => { });

        Assert.NotNull(vtProcessor.ResponseCallback);

        vtProcessor.ResponseCallback!("\x1b[0n"u8.ToArray());

        Assert.NotNull(transport.LastInputBytes);
        Assert.Equal("\x1b[0n", Encoding.ASCII.GetString(transport.LastInputBytes));

        await service.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task StartSessionAsync_WhenPriorTransportExited_ReleasesStaleTransportAndStartsNewSession()
    {
        TerminalSessionService service = new();
        FakeTransport firstTransport = new();
        FakeTransport secondTransport = new();
        FixedTransportFactory firstFactory = new(firstTransport);
        FixedTransportFactory secondFactory = new(secondTransport);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            firstFactory,
            new FakeTransportOptions(TerminalTransportIds.Pty),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        Assert.True(service.HasActiveTransport);
        firstTransport.CompleteProcess(exitCode: 0);
        Assert.False(service.HasActiveTransport);

        await service.StartSessionAsync(
            secondFactory,
            new FakeTransportOptions(TerminalTransportIds.Ssh),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        Assert.True(firstTransport.DisposeCalled);
        Assert.Same(secondTransport, service.Transport);
        Assert.True(service.HasActiveTransport);

        await service.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task SendInput_WhenPriorTransportExited_ClearsActiveVtCallbacks()
    {
        TerminalSessionService service = new();
        FakeTransport transport = new();
        FixedTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await service.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Ssh),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        Assert.NotNull(vtProcessor.ResponseCallback);
        Assert.NotNull(vtProcessor.BellCallback);
        Assert.NotNull(vtProcessor.TitleCallback);

        transport.CompleteProcess(exitCode: 0);
        Assert.False(service.HasActiveTransport);

        service.SendInput("ignored");

        Assert.Null(vtProcessor.ResponseCallback);
        Assert.Null(vtProcessor.BellCallback);
        Assert.Null(vtProcessor.TitleCallback);
        Assert.Null(service.Transport);
    }

    private sealed record FakeTransportOptions(string TransportId) : ITerminalTransportOptions
    {
        public TerminalSessionDimensions Dimensions => new(80, 24, 640, 480);
    }

    private sealed class FixedTransportFactory : ITerminalTransportFactory
    {
        private readonly ITerminalTransport _transport;

        public FixedTransportFactory(ITerminalTransport transport)
        {
            _transport = transport;
        }

        public ITerminalTransport Create(ITerminalTransportOptions options)
        {
            _ = options;
            return _transport;
        }
    }

    private sealed class ThrowingFactory : ITerminalTransportFactory
    {
        public ITerminalTransport Create(ITerminalTransportOptions options)
        {
            _ = options;
            throw new InvalidOperationException("Simulated transport factory failure.");
        }
    }

    private sealed class FakeTransport : ITerminalTransport
    {
        private Action<byte[], int>? _dataReceived;
        private Action<int>? _processExited;

        public event Action<byte[], int>? DataReceived
        {
            add
            {
                _dataReceived += value;
                DataSubscriberCount++;
            }
            remove
            {
                _dataReceived -= value;
                DataSubscriberCount--;
            }
        }

        public event Action<int>? ProcessExited
        {
            add
            {
                _processExited += value;
                ExitSubscriberCount++;
            }
            remove
            {
                _processExited -= value;
                ExitSubscriberCount--;
            }
        }

        public bool IsRunning { get; private set; }

        public bool ThrowOnStart { get; set; }

        public byte[]? EmitDataDuringStart { get; init; }

        public bool StopCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public int DataSubscriberCount { get; private set; }

        public int ExitSubscriberCount { get; private set; }

        public byte[]? LastInputBytes { get; private set; }

        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        {
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnStart)
            {
                throw new InvalidOperationException("Simulated transport start failure.");
            }

            IsRunning = true;

            if (EmitDataDuringStart is not null)
            {
                _dataReceived?.Invoke(EmitDataDuringStart, EmitDataDuringStart.Length);
            }

            return ValueTask.CompletedTask;
        }

        public void SendInput(ReadOnlySpan<byte> utf8)
        {
            LastInputBytes = utf8.ToArray();
        }

        public void Resize(TerminalSessionDimensions dimensions)
        {
            _ = dimensions;
        }

        public ValueTask StopAsync()
        {
            StopCalled = true;
            IsRunning = false;
            _processExited?.Invoke(0);
            return ValueTask.CompletedTask;
        }

        public void CompleteProcess(int exitCode)
        {
            IsRunning = false;
            _processExited?.Invoke(exitCode);
        }

        public void Dispose()
        {
            DisposeCalled = true;
            IsRunning = false;
        }
    }

    private sealed class FakeVtProcessor : IVtProcessor
    {
        private TerminalModeState _modeState = new(
            CursorVisible: true,
            ApplicationCursorKeys: false,
            ApplicationKeypad: false,
            AlternateScreen: false,
            BracketedPaste: false);

        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => _modeState.CursorVisible;
        public bool ApplicationCursorKeys => _modeState.ApplicationCursorKeys;
        public bool ApplicationKeypad => _modeState.ApplicationKeypad;
        public bool AlternateScreen => _modeState.AlternateScreen;
        public bool BracketedPaste => _modeState.BracketedPaste;

        public TerminalModeState ModeState => _modeState;

        public event EventHandler<TerminalModeState>? ModeChanged;

        public Action<byte[]>? ResponseCallback { get; set; }

        public Action? BellCallback { get; set; }

        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = rows;
            _ = widthPx;
            _ = heightPx;
        }

        public void Reset()
        {
        }

        public void SetModeState(TerminalModeState state)
        {
            _modeState = state;
            ModeChanged?.Invoke(this, state);
        }

        public void Dispose()
        {
        }
    }
}
