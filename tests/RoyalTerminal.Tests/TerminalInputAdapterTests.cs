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
    public void HandleKeyDown_WithInputSink_CtrlC_RoutesToKeySink()
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
            Key = Key.C,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.Equal(1, endpoint.KeyCallCount);
        Assert.Null(endpoint.LastUtf8Input);
        Assert.Equal(Key.C, (Key)endpoint.LastKeyEvent!.Value.KeyCode);
        Assert.Equal(TerminalModifiers.Control, endpoint.LastKeyEvent.Value.Modifiers);
    }

    [Fact]
    public void HandleKeyDown_WithEndpointWithoutInputSink_UsesByteFallback()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        ByteOnlyEndpoint endpoint = new();
        sessionService.AttachEndpoint(endpoint);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Return,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(endpoint.LastInput);
        Assert.Equal("\r", Encoding.UTF8.GetString(endpoint.LastInput!));
    }

    [Fact]
    public void HandleTextInput_WithEndpointWithoutInputSink_UsesByteFallback()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        ByteOnlyEndpoint endpoint = new();
        sessionService.AttachEndpoint(endpoint);

        TextInputEventArgs textInputEventArgs = new()
        {
            Text = "abc",
        };

        bool handled = adapter.HandleTextInput(textInputEventArgs, sessionService);

        Assert.True(handled);
        Assert.NotNull(endpoint.LastInput);
        Assert.Equal("abc", Encoding.UTF8.GetString(endpoint.LastInput!));
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

    [Fact]
    public async Task HandleKeyDown_WithWindowsWin32InputMode_EncodesWin32InputRecord()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { Win32InputMode = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.A,
            KeyModifiers = KeyModifiers.None,
            KeySymbol = "a",
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        string[] fields = ParseWin32InputFields(transport.LastInput!);
        Assert.Equal("65", fields[0]); // VK_A
        Assert.Equal("97", fields[2]); // 'a'
        Assert.Equal("1", fields[3]);  // key down
        Assert.Equal("0", fields[4]);  // control key state
        Assert.Equal("1", fields[5]);  // repeat count

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyUp_WithWindowsWin32InputMode_EncodesWin32InputRecord()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { Win32InputMode = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.A,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyUp(keyEventArgs, sessionService);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        string[] fields = ParseWin32InputFields(transport.LastInput!);
        Assert.Equal("65", fields[0]); // VK_A
        Assert.Equal("0", fields[2]);  // no UnicodeChar on key up
        Assert.Equal("0", fields[3]);  // key up
        Assert.Equal("1", fields[5]);  // repeat count

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleTextInput_WithWindowsWin32InputMode_DoesNotSendDuplicateText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { Win32InputMode = true });

        TextInputEventArgs textInputEventArgs = new()
        {
            Text = "a",
        };

        bool handled = adapter.HandleTextInput(textInputEventArgs, sessionService);

        Assert.True(handled);
        Assert.Null(transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithWindowsWin32InputMode_CtrlC_EncodesWin32InputRecord()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { Win32InputMode = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.C,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.NotEqual(new byte[] { 0x03 }, transport.LastInput);
        string[] fields = ParseWin32InputFields(transport.LastInput!);
        Assert.Equal("67", fields[0]); // VK_C
        Assert.Equal("1", fields[3]);  // key down

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_UsesSessionModeSourceForApplicationCursorMode()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { ApplicationCursorKeys = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Up,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1BOA", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_UsesSessionModeSourceForBackarrowKeyMode()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { BackarrowKeyMode = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Back,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x08 }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesModifierAwareArrowKeys()
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
            Key = Key.Up,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1B[1;6A", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithNativeKeyEncoder_UsesNativeSequence()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new()
        {
            EncodedKeySequence = "native-down"u8.ToArray(),
        };
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Up,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.Equal("native-down", Encoding.UTF8.GetString(transport.LastInput!));
        Assert.Single(vtProcessor.EncodedKeyRequests);
        Assert.Equal("Up", vtProcessor.EncodedKeyRequests[0].KeyId);
        Assert.Equal(TerminalInputAction.Press, vtProcessor.EncodedKeyRequests[0].Action);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyUp_WithNativeKeyEncoder_UsesModeSourceDelegate()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new()
        {
            EncodedKeySequence = "native-up"u8.ToArray(),
        };
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.A,
            KeyModifiers = KeyModifiers.Shift,
        };

        bool handled = adapter.HandleKeyUp(keyEventArgs, sessionService);

        Assert.True(handled);
        Assert.Equal("native-up", Encoding.UTF8.GetString(transport.LastInput!));
        Assert.Single(vtProcessor.EncodedKeyRequests);
        Assert.Equal("A", vtProcessor.EncodedKeyRequests[0].KeyId);
        Assert.Equal(TerminalInputAction.Release, vtProcessor.EncodedKeyRequests[0].Action);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithNativeKeyEncoder_PlainPrintableKey_DefersToTextInput()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new()
        {
            EncodedKeySequence = "native-a"u8.ToArray(),
        };
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.A,
            KeyModifiers = KeyModifiers.None,
            KeySymbol = "a",
        };

        bool keyHandled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.False(keyHandled);
        Assert.Empty(vtProcessor.EncodedKeyRequests);
        Assert.Null(transport.LastInput);

        TextInputEventArgs textInputEventArgs = new()
        {
            Text = "a",
        };

        bool textHandled = adapter.HandleTextInput(textInputEventArgs, sessionService);

        Assert.True(textHandled);
        Assert.Equal("a", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithWindowsAltGrPrintableKey_DefersToTextInput()
    {
        DefaultTerminalInputAdapter adapter = new(new WindowsTerminalKeyboardInputNormalizer());
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        adapter.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.RightAlt,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService,
            vtProcessor: null);

        bool keyHandled = adapter.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.D3,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService,
            vtProcessor);

        Assert.False(keyHandled);
        Assert.Null(transport.LastInput);

        bool textHandled = adapter.HandleTextInput(new TextInputEventArgs { Text = "#" }, sessionService);

        Assert.True(textHandled);
        Assert.Equal("#", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithWindowsAltGrDigitWhenRightAltWasSwallowed_DefersToTextInput()
    {
        DefaultTerminalInputAdapter adapter = new(new WindowsTerminalKeyboardInputNormalizer());
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        bool keyHandled = adapter.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.D4,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService,
            vtProcessor);

        Assert.False(keyHandled);
        Assert.Null(transport.LastInput);

        bool textHandled = adapter.HandleTextInput(new TextInputEventArgs { Text = "{" }, sessionService);

        Assert.True(textHandled);
        Assert.Equal("{", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithWindowsAltGrEuroWhenRightAltWasSwallowed_DefersToTextInput()
    {
        DefaultTerminalInputAdapter adapter = new(new WindowsTerminalKeyboardInputNormalizer());
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        bool keyHandled = adapter.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.E,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService,
            vtProcessor);

        Assert.False(keyHandled);
        Assert.Null(transport.LastInput);

        bool textHandled = adapter.HandleTextInput(new TextInputEventArgs { Text = "€" }, sessionService);

        Assert.True(textHandled);
        Assert.Equal("€", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyUp_WithWindowsAltGrPrintableKey_DoesNotSendRelease()
    {
        DefaultTerminalInputAdapter adapter = new(new WindowsTerminalKeyboardInputNormalizer());
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new()
        {
            EncodedKeySequence = "native"u8.ToArray(),
        };
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        adapter.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.RightAlt,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService,
            vtProcessor: null);

        adapter.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.E,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService,
            vtProcessor: null);

        int encodedRequestCount = vtProcessor.EncodedKeyRequests.Count;
        bool keyUpHandled = adapter.HandleKeyUp(
            new KeyEventArgs
            {
                Key = Key.E,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            sessionService);

        Assert.False(keyUpHandled);
        Assert.Equal(encodedRequestCount, vtProcessor.EncodedKeyRequests.Count);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithWindowsNormalizer_RealControlAltChordStillEncodes()
    {
        DefaultTerminalInputAdapter adapter = new(new WindowsTerminalKeyboardInputNormalizer());
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
            Key = Key.C,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            KeySymbol = "c",
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x1B, 0x03 }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public void WindowsNormalizer_Win32InputMode_DoesNotSuppressAltGrPrintableKey()
    {
        WindowsTerminalKeyboardInputNormalizer normalizer = new(new FixedWindowsKeyboardLayoutTextInputProbe(mayProduceText: true));
        TerminalModeState modeState = new FakeVtProcessor().ModeState with { Win32InputMode = true };

        TerminalKeyboardInputAction rightAltAction = normalizer.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.RightAlt,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            modeState);
        TerminalKeyboardInputAction textKeyAction = normalizer.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.D3,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            modeState);

        Assert.Equal(TerminalKeyboardInputAction.Forward, rightAltAction);
        Assert.Equal(TerminalKeyboardInputAction.Forward, textKeyAction);
    }

    [Fact]
    public void WindowsNormalizer_LayoutProbeTextKey_SuppressesCtrlAltKey()
    {
        WindowsTerminalKeyboardInputNormalizer normalizer = new(new FixedWindowsKeyboardLayoutTextInputProbe(mayProduceText: true));

        TerminalKeyboardInputAction action = normalizer.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.Q,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            new FakeVtProcessor().ModeState);

        Assert.Equal(TerminalKeyboardInputAction.SuppressForTextInput, action);
    }

    [Fact]
    public void WindowsNormalizer_LayoutProbeNonTextLetter_ForwardsCtrlAltKey()
    {
        WindowsTerminalKeyboardInputNormalizer normalizer = new(new FixedWindowsKeyboardLayoutTextInputProbe(mayProduceText: false));

        TerminalKeyboardInputAction action = normalizer.HandleKeyDown(
            new KeyEventArgs
            {
                Key = Key.Q,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            },
            new FakeVtProcessor().ModeState);

        Assert.Equal(TerminalKeyboardInputAction.Forward, action);
    }

    [Fact]
    public async Task HandleKeyDown_ApplicationCursorMode_WithModifiers_UsesCsiEncoding()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { ApplicationCursorKeys = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Up,
            KeyModifiers = KeyModifiers.Shift,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1B[1;2A", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesModifierAwareFunctionKeys()
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
            Key = Key.F5,
            KeyModifiers = KeyModifiers.Alt,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1B[15;3~", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesControlPunctuation()
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
            Key = Key.OemOpenBrackets,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x1B }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesCtrlSpaceAsNul()
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
            Key = Key.Space,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x00 }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithKittyKeyboardDisambiguation_EncodesCtrlChordAsCsiU()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.I,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1B[105;5u", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithKittyKeyboardDisambiguation_UsesModeSourceWhenProcessorArgumentIsNull()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Up,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1B[57352u", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithKittyKeyboardDisambiguation_CtrlC_UsesEncoderOutput()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.C,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        string encoded = Encoding.UTF8.GetString(transport.LastInput!);
        Assert.StartsWith("\x1B[", encoded, StringComparison.Ordinal);
        Assert.EndsWith("u", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("\x03", encoded, StringComparison.Ordinal);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithKittyKeyboardDisambiguation_CtrlZ_UsesEncoderOutput()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetKittyKeyboardFlags(1);

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.Z,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        string encoded = Encoding.UTF8.GetString(transport.LastInput!);
        Assert.StartsWith("\x1B[", encoded, StringComparison.Ordinal);
        Assert.EndsWith("u", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("\x1A", encoded, StringComparison.Ordinal);

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesAltControlChord()
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
            Key = Key.C,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x1B, 0x03 }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesAltSpace()
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
            Key = Key.Space,
            KeyModifiers = KeyModifiers.Alt,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x1B, 0x20 }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_UsesSessionModeSourceForApplicationKeypadMode()
    {
        DefaultTerminalInputAdapter adapter = new();
        TerminalSessionService sessionService = new();
        FakeTransport transport = new();
        StaticTransportFactory factory = new(transport);
        FakeVtProcessor vtProcessor = new();
        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };

        await sessionService.StartSessionAsync(
            factory,
            new FakeTransportOptions(TerminalTransportIds.Pipe),
            vtProcessor,
            onData,
            onExit,
            _ => { },
            () => { },
            _ => { });

        vtProcessor.SetModeState(vtProcessor.ModeState with { ApplicationKeypad = true });

        KeyEventArgs keyEventArgs = new()
        {
            Key = Key.NumPad1,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1BOq", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesNormalKeypadDigits()
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
            Key = Key.NumPad1,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("1", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesPlainTabAsHorizontalTab()
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
            Key = Key.Tab,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal(new byte[] { 0x09 }, transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_EncodesShiftTabAsBacktab()
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
            Key = Key.Tab,
            KeyModifiers = KeyModifiers.Shift,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.NotNull(transport.LastInput);
        Assert.Equal("\x1B[Z", Encoding.UTF8.GetString(transport.LastInput!));

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_DoesNotHandleCtrlTab()
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
            Key = Key.Tab,
            KeyModifiers = KeyModifiers.Control,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.Null(transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_DoesNotHandleMetaModifiedNavigationKey()
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
            Key = Key.Up,
            KeyModifiers = KeyModifiers.Meta,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.Null(transport.LastInput);

        await sessionService.StopSessionAsync(vtProcessor: null, onData, onExit);
    }

    [Fact]
    public async Task HandleKeyDown_WithActiveTransport_DoesNotHandlePrintableKeyOnKeyDownPath()
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
            Key = Key.A,
            KeyModifiers = KeyModifiers.None,
        };

        bool handled = adapter.HandleKeyDown(keyEventArgs, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.Null(transport.LastInput);

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
        public byte[]? LastUtf8Input { get; private set; }

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
            LastUtf8Input = utf8.ToArray();
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

    private sealed class ByteOnlyEndpoint : ITerminalEndpoint
    {
        public byte[]? LastInput { get; private set; }

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            LastInput = utf8.ToArray();
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

    private sealed class FixedWindowsKeyboardLayoutTextInputProbe : IWindowsKeyboardLayoutTextInputProbe
    {
        private readonly bool _mayProduceText;

        public FixedWindowsKeyboardLayoutTextInputProbe(bool mayProduceText)
        {
            _mayProduceText = mayProduceText;
        }

        public bool MayProduceText(KeyEventArgs e)
        {
            _ = e;
            return _mayProduceText;
        }
    }

    private static string[] ParseWin32InputFields(byte[] bytes)
    {
        string sequence = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("\x1B[", sequence);
        Assert.EndsWith("_", sequence);

        string payload = sequence.Substring(2, sequence.Length - 3);
        string[] fields = payload.Split(';');
        Assert.Equal(6, fields.Length);
        return fields;
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

    private sealed class FakeVtProcessor : IVtProcessor, IKittyKeyboardStateSource, ITerminalKeySequenceEncoderSource
    {
        private TerminalModeState _modeState = new(
            CursorVisible: true,
            ApplicationCursorKeys: false,
            ApplicationKeypad: false,
            AlternateScreen: false,
            BracketedPaste: false);
        private int _kittyKeyboardFlags;
        public List<TerminalKeyEncodingRequest> EncodedKeyRequests { get; } = [];
        public byte[]? EncodedKeySequence { get; init; }

        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => _modeState.CursorVisible;
        public bool ApplicationCursorKeys => _modeState.ApplicationCursorKeys;
        public bool ApplicationKeypad => _modeState.ApplicationKeypad;
        public bool AlternateScreen => _modeState.AlternateScreen;
        public bool BracketedPaste => _modeState.BracketedPaste;
        public bool Win32InputMode => _modeState.Win32InputMode;
        public int KittyKeyboardFlags => _kittyKeyboardFlags;
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

        public void SetKittyKeyboardFlags(int flags)
        {
            _kittyKeyboardFlags = flags < 0 ? 0 : flags;
        }

        public bool TryEncodeKey(in TerminalKeyEncodingRequest request, out byte[] sequence)
        {
            EncodedKeyRequests.Add(request);
            if (EncodedKeySequence is not null)
            {
                sequence = EncodedKeySequence;
                return true;
            }

            sequence = [];
            return false;
        }

        public void Dispose()
        {
        }
    }
}
