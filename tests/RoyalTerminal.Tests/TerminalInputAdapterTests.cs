// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
}
