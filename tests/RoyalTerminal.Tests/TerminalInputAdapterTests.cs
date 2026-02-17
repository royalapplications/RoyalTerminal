// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Input;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalInputAdapterTests
{
    [Fact]
    public void HandleKeyDown_ReturnsInputSinkAcceptance()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeEndpoint endpoint = new()
        {
            KeyResult = false,
        };
        sessionService.AttachEndpoint(endpoint);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.A,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.Equal(1, endpoint.KeyCallCount);
        Assert.Equal(TerminalInputAction.Press, endpoint.LastKeyEvent?.Action);
    }

    [Fact]
    public void HandleKeyUp_ReturnsInputSinkAcceptance()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeEndpoint endpoint = new()
        {
            KeyResult = false,
        };
        sessionService.AttachEndpoint(endpoint);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.A,
            KeyModifiers = KeyModifiers.Shift,
        };

        bool handled = adapter.HandleKeyUp(keyEventArgs, sessionService);

        Assert.False(handled);
        Assert.Equal(1, endpoint.KeyCallCount);
        Assert.Equal(TerminalInputAction.Release, endpoint.LastKeyEvent?.Action);
    }

    [Fact]
    public void HandleTextInput_ReturnsInputSinkAcceptance()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeEndpoint endpoint = new()
        {
            TextResult = false,
        };
        sessionService.AttachEndpoint(endpoint);

        TextInputEventArgs textInputEventArgs = new()
        {
            Text = "abc",
        };

        bool handled = adapter.HandleTextInput(textInputEventArgs, sessionService);

        Assert.False(handled);
        Assert.Equal(1, endpoint.TextCallCount);
        Assert.Equal("abc", endpoint.LastTextInput);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_UsesSessionFallbackWrite()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Return,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\r", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleTextInput_WithActiveTransport_ReturnsTrueAndWritesInput()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor: null,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        TextInputEventArgs textInputEventArgs = new()
        {
            Text = "abc",
        };

        bool handled = adapter.HandleTextInput(textInputEventArgs, sessionService);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("abc", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    private sealed class FakeEndpoint : ITerminalEndpoint, ITerminalInputSink
    {
        public bool KeyResult { get; set; } = true;

        public bool TextResult { get; set; } = true;

        public int KeyCallCount { get; private set; }

        public int TextCallCount { get; private set; }

        public TerminalKeyEvent? LastKeyEvent { get; private set; }

        public string? LastTextInput { get; private set; }

        public bool SendKey(TerminalKeyEvent keyEvent)
        {
            KeyCallCount++;
            LastKeyEvent = keyEvent;
            return KeyResult;
        }

        public bool SendText(string text)
        {
            TextCallCount++;
            LastTextInput = text;
            return TextResult;
        }

        public bool SendPointer(TerminalPointerEvent pointerEvent)
        {
            _ = pointerEvent;
            return true;
        }

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            _ = utf8;
        }

        public void SetFocus(bool focused)
        {
            _ = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            _ = widthPx;
            _ = heightPx;
        }
    }

    private sealed record FakeTransportOptions(string TransportId) : ITerminalTransportOptions
    {
        public TerminalSessionDimensions Dimensions => new(80, 24, 640, 480);
    }

    private sealed class StaticTransportFactory : ITerminalTransportFactory
    {
        private readonly ITerminalTransport _transport;

        public StaticTransportFactory(ITerminalTransport transport)
        {
            _transport = transport;
        }

        public ITerminalTransport Create(ITerminalTransportOptions options)
        {
            _ = options;
            return _transport;
        }
    }

    private sealed class FakeTransport : ITerminalTransport
    {
        private Action<byte[], int>? _dataReceived;
        private Action<int>? _processExited;

        public event Action<byte[], int>? DataReceived
        {
            add => _dataReceived += value;
            remove => _dataReceived -= value;
        }

        public event Action<int>? ProcessExited
        {
            add => _processExited += value;
            remove => _processExited -= value;
        }

        public bool IsRunning { get; private set; }

        public byte[]? LastInput { get; private set; }

        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        {
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            IsRunning = true;
            return ValueTask.CompletedTask;
        }

        public void SendInput(ReadOnlySpan<byte> utf8)
        {
            LastInput = utf8.ToArray();
            _dataReceived?.Invoke(LastInput, LastInput.Length);
        }

        public void Resize(TerminalSessionDimensions dimensions)
        {
            _ = dimensions;
        }

        public ValueTask StopAsync()
        {
            IsRunning = false;
            _processExited?.Invoke(0);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }
}
