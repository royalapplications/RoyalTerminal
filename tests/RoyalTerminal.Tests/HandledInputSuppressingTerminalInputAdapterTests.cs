using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class HandledInputSuppressingTerminalInputAdapterTests
{
    private const int KittyReportEvents = 0x02;
    private const int KittyReportAllKeysAsEscapeCodes = 0x08;

    [Fact]
    public void HandleKeyDown_DoesNotDelegateIsolatedShift_WhenKittyFlagsAreAbsent()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        KeyEventArgs args = CreateKeyEventArgs(Key.LeftShift);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.True(args.Handled);
        Assert.Equal(0, inner.KeyDownCount);
    }

    [Fact]
    public void HandleKeyUp_DoesNotDelegateIsolatedShift_WhenKittyFlagsAreAbsent()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        KeyEventArgs args = CreateKeyEventArgs(Key.LeftShift);

        bool handled = adapter.HandleKeyUp(args, sessionService);

        Assert.False(handled);
        Assert.True(args.Handled);
        Assert.Equal(0, inner.KeyUpCount);
    }

    [Fact]
    public void HandleKeyDown_DelegatesIsolatedShift_WhenKittyReportsAllKeyStates()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestKittyModeSource(KittyReportEvents | KittyReportAllKeysAsEscapeCodes));
        KeyEventArgs args = CreateKeyEventArgs(Key.LeftShift);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(1, inner.KeyDownCount);
        Assert.Equal(KeyModifiers.Shift, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void HandleKeyDown_SendsWin32InputSequenceForIsolatedShift_WhenWin32InputModeIsEnabled()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestModeSource(new TerminalModeState { Win32InputMode = true }));
        KeyEventArgs args = CreateKeyEventArgs(Key.LeftShift, KeyModifiers.Shift);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(0, inner.KeyDownCount);
        Assert.Equal(["\u001b[16;42;0;1;16;1_"], sessionService.SentText);
    }

    [Fact]
    public void HandleKeyUp_SendsWin32InputSequenceForIsolatedShift_WhenWin32InputModeIsEnabled()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestModeSource(new TerminalModeState { Win32InputMode = true }));
        KeyEventArgs args = CreateKeyEventArgs(Key.LeftShift);

        bool handled = adapter.HandleKeyUp(args, sessionService);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(0, inner.KeyUpCount);
        Assert.Equal(["\u001b[16;42;0;0;0;1_"], sessionService.SentText);
    }

    [Fact]
    public void HandleKeyDown_SendsWin32InputSequenceAndHandlesEventForIsolatedAlt_WhenWin32InputModeIsEnabled()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestModeSource(new TerminalModeState { Win32InputMode = true }));
        KeyEventArgs args = CreateKeyEventArgs(Key.LeftAlt, KeyModifiers.Alt);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(0, inner.KeyDownCount);
        Assert.Equal(["\u001b[18;56;0;1;2;1_"], sessionService.SentText);
    }

    [Fact]
    public void HandleKeyDown_SendsRightAltOnlyWin32InputSequence_WhenAvaloniaReportsGenericAltModifier()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestModeSource(new TerminalModeState { Win32InputMode = true }));
        KeyEventArgs args = CreateKeyEventArgs(Key.RightAlt, KeyModifiers.Alt);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(0, inner.KeyDownCount);
        Assert.Equal(["\u001b[18;56;0;1;257;1_"], sessionService.SentText);
    }

    [Fact]
    public void HandleKeyDown_DelegatesHandledIsolatedAlt_WhenKittyReportsAllKeyStates()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestKittyModeSource(KittyReportEvents | KittyReportAllKeysAsEscapeCodes));
        KeyEventArgs args = CreateHandledKeyEventArgs(Key.LeftAlt);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(1, inner.KeyDownCount);
        Assert.Equal(KeyModifiers.Alt, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void HandleKeyDown_DelegatesHandledIsolatedAlt_WhenVtProcessorReportsKittyAllKeyStates()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        TestKittyVtProcessor vtProcessor = new(KittyReportEvents | KittyReportAllKeysAsEscapeCodes);
        KeyEventArgs args = CreateHandledKeyEventArgs(Key.LeftAlt);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor);

        Assert.True(handled);
        Assert.True(args.Handled);
        Assert.Equal(1, inner.KeyDownCount);
        Assert.Equal(KeyModifiers.Alt, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void HandleKeyDown_AddsTrackedAltModifierToSubsequentKey_WhenVtProcessorReportsKittyAllKeyStates()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        TestKittyVtProcessor vtProcessor = new(KittyReportEvents | KittyReportAllKeysAsEscapeCodes);

        bool altHandled = adapter.HandleKeyDown(
            CreateHandledKeyEventArgs(Key.LeftAlt),
            sessionService,
            vtProcessor);
        bool keyHandled = adapter.HandleKeyDown(
            CreateKeyEventArgs(Key.A),
            sessionService,
            vtProcessor);

        Assert.True(altHandled);
        Assert.True(keyHandled);
        Assert.Equal(2, inner.KeyDownCount);
        Assert.Equal(Key.A, inner.LastKeyDownKey);
        Assert.Equal(KeyModifiers.Alt, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void ResetInputState_ClearsTrackedAltModifier_WhenKeyUpWasLost()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        TestKittyVtProcessor vtProcessor = new(KittyReportEvents | KittyReportAllKeysAsEscapeCodes);

        Assert.True(adapter.HandleKeyDown(CreateHandledKeyEventArgs(Key.LeftAlt), sessionService, vtProcessor));
        adapter.ResetInputState();

        bool keyHandled = adapter.HandleKeyDown(CreateKeyEventArgs(Key.Q), sessionService, vtProcessor);

        Assert.True(keyHandled);
        Assert.Equal(Key.Q, inner.LastKeyDownKey);
        Assert.Equal(KeyModifiers.None, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void ResetInputState_ClearsTrackedAltModifier_WhenWin32InputModeKeyUpWasLost()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestModeSource(new TerminalModeState { Win32InputMode = true }));

        Assert.True(adapter.HandleKeyDown(CreateHandledKeyEventArgs(Key.LeftAlt), sessionService, vtProcessor: null));
        adapter.ResetInputState();

        bool keyHandled = adapter.HandleKeyDown(CreateKeyEventArgs(Key.Q), sessionService, vtProcessor: null);

        Assert.True(keyHandled);
        Assert.Equal(Key.Q, inner.LastKeyDownKey);
        Assert.Equal(KeyModifiers.None, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void HandleKeyDown_DoesNotAddTrackedAltModifierAfterAltKeyUp()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        TestKittyVtProcessor vtProcessor = new(KittyReportEvents | KittyReportAllKeysAsEscapeCodes);

        Assert.True(adapter.HandleKeyDown(CreateHandledKeyEventArgs(Key.LeftAlt), sessionService, vtProcessor));
        Assert.True(adapter.HandleKeyUp(CreateHandledKeyEventArgs(Key.LeftAlt, KeyModifiers.Alt), sessionService));

        bool keyHandled = adapter.HandleKeyDown(CreateKeyEventArgs(Key.A), sessionService, vtProcessor);

        Assert.True(keyHandled);
        Assert.Equal(Key.A, inner.LastKeyDownKey);
        Assert.Equal(KeyModifiers.None, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void HandleKeyDown_AddsTrackedAltModifierToSubsequentKey_WhenWin32InputModeIsEnabled()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new(
            new TestModeSource(new TerminalModeState { Win32InputMode = true }));

        Assert.True(adapter.HandleKeyDown(CreateHandledKeyEventArgs(Key.LeftAlt), sessionService, vtProcessor: null));
        bool keyHandled = adapter.HandleKeyDown(CreateKeyEventArgs(Key.A), sessionService, vtProcessor: null);

        Assert.True(keyHandled);
        Assert.Equal(1, inner.KeyDownCount);
        Assert.Equal(Key.A, inner.LastKeyDownKey);
        Assert.Equal(KeyModifiers.Alt, inner.LastKeyDownModifiers);
    }

    [Fact]
    public void HandleKeyDown_DoesNotDelegateHandledNonModifierKey()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        KeyEventArgs args = CreateHandledKeyEventArgs(Key.A);

        bool handled = adapter.HandleKeyDown(args, sessionService, vtProcessor: null);

        Assert.False(handled);
        Assert.Equal(0, inner.KeyDownCount);
    }

    [Fact]
    public void HandleTextInput_DelegatesText_WhenKittyFlagsAreAbsent()
    {
        RecordingTerminalInputAdapter inner = new();
        HandledInputSuppressingTerminalInputAdapter adapter = new(inner);
        TestTerminalSessionService sessionService = new();
        TextInputEventArgs args = new() { Text = "$" };

        bool handled = adapter.HandleTextInput(args, sessionService);

        Assert.True(handled);
        Assert.Equal(1, inner.TextInputCount);
    }

    private static KeyEventArgs CreateKeyEventArgs(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None,
        string? keySymbol = null) =>
        new()
        {
            Key = key,
            KeyModifiers = modifiers,
            KeySymbol = keySymbol,
        };

    private static KeyEventArgs CreateHandledKeyEventArgs(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None,
        string? keySymbol = null)
    {
        KeyEventArgs args = CreateKeyEventArgs(key, modifiers, keySymbol);
        args.Handled = true;
        return args;
    }

    private sealed class RecordingTerminalInputAdapter : ITerminalInputAdapter
    {
        public int KeyDownCount { get; private set; }

        public int KeyUpCount { get; private set; }

        public int TextInputCount { get; private set; }

        public Key LastKeyDownKey { get; private set; }

        public KeyModifiers LastKeyDownModifiers { get; private set; }

        public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
        {
            _ = sessionService;
            _ = vtProcessor;
            KeyDownCount++;
            LastKeyDownKey = e.Key;
            LastKeyDownModifiers = e.KeyModifiers;
            return true;
        }

        public bool HandleKeyUp(KeyEventArgs e, ITerminalSessionService sessionService)
        {
            _ = e;
            _ = sessionService;
            KeyUpCount++;
            return true;
        }

        public bool HandleTextInput(TextInputEventArgs e, ITerminalSessionService sessionService)
        {
            _ = e;
            _ = sessionService;
            TextInputCount++;
            return true;
        }
    }

    private sealed class TestKittyModeSource : ITerminalModeSource, IKittyKeyboardStateSource
    {
        public TestKittyModeSource(int kittyKeyboardFlags)
        {
            KittyKeyboardFlags = kittyKeyboardFlags;
        }

        public TerminalModeState ModeState => default;

        public int KittyKeyboardFlags { get; }

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class TestModeSource : ITerminalModeSource
    {
        public TestModeSource(TerminalModeState modeState)
        {
            ModeState = modeState;
        }

        public TerminalModeState ModeState { get; }

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class TestKittyVtProcessor : IVtProcessor, IKittyKeyboardStateSource
    {
        public TestKittyVtProcessor(int kittyKeyboardFlags)
        {
            KittyKeyboardFlags = kittyKeyboardFlags;
        }

        public int KittyKeyboardFlags { get; }

        public int CursorCol => 0;

        public int CursorRow => 0;

        public bool CursorVisible => true;

        public bool ApplicationCursorKeys => false;

        public bool ApplicationKeypad => false;

        public bool AlternateScreen => false;

        public bool BracketedPaste => false;

        public bool Win32InputMode => false;

        public TerminalModeState ModeState => default;

        public Action<byte[]>? ResponseCallback { get; set; }

        public Action? BellCallback { get; set; }

        public Action<string>? TitleCallback { get; set; }

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

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

        public void Dispose()
        {
        }
    }

    private sealed class TestTerminalSessionService : ITerminalSessionService
    {
        public TestTerminalSessionService(ITerminalModeSource? modeSource = null)
        {
            ModeSource = modeSource ?? new TestModeSource(default);
        }

        public List<string> SentText { get; } = [];

        public ITerminalEndpoint? Endpoint => null;

        public ITerminalInputSink? InputSink => null;

        public ITerminalSelectionSource? SelectionSource => null;

        public ITerminalModeSource? ModeSource { get; }

        public ITerminalTransport? Transport => null;

        public bool HasActiveTransport => false;

        public IPty? Pty => null;

        public bool HasPty => false;

        public event EventHandler<TerminalSessionInputEventArgs>? InputSent
        {
            add { }
            remove { }
        }

        public void AttachEndpoint(ITerminalEndpoint endpoint)
        {
            _ = endpoint;
        }

        public void DetachEndpoint()
        {
        }

        public void SendInput(string text)
        {
            SentText.Add(text);
        }

        public void SendInput(ReadOnlySpan<byte> data)
        {
            _ = data;
        }

        public void StartPty(
            IPtyFactory ptyFactory,
            string? shell,
            int columns,
            int rows,
            string? workingDirectory,
            IVtProcessor? vtProcessor,
            Action<byte[], int> onPtyDataReceived,
            Action<int> onPtyProcessExited,
            Action<byte[]> onVtResponse,
            Action onVtBell,
            Action<string> onVtTitleChanged,
            IReadOnlyList<string>? arguments = null)
        {
            _ = ptyFactory;
            _ = shell;
            _ = columns;
            _ = rows;
            _ = workingDirectory;
            _ = vtProcessor;
            _ = onPtyDataReceived;
            _ = onPtyProcessExited;
            _ = onVtResponse;
            _ = onVtBell;
            _ = onVtTitleChanged;
            _ = arguments;
        }

        public ValueTask StartSessionAsync(
            ITerminalTransportFactory transportFactory,
            ITerminalTransportOptions transportOptions,
            IVtProcessor? vtProcessor,
            Action<byte[], int> onTransportDataReceived,
            Action<int> onTransportProcessExited,
            Action<byte[]> onVtResponse,
            Action onVtBell,
            Action<string> onVtTitleChanged,
            CancellationToken cancellationToken = default)
        {
            _ = transportFactory;
            _ = transportOptions;
            _ = vtProcessor;
            _ = onTransportDataReceived;
            _ = onTransportProcessExited;
            _ = onVtResponse;
            _ = onVtBell;
            _ = onVtTitleChanged;
            _ = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public void StopPty(
            IVtProcessor? vtProcessor,
            Action<byte[], int> onPtyDataReceived,
            Action<int> onPtyProcessExited)
        {
            _ = vtProcessor;
            _ = onPtyDataReceived;
            _ = onPtyProcessExited;
        }

        public ValueTask StopSessionAsync(
            IVtProcessor? vtProcessor,
            Action<byte[], int> onTransportDataReceived,
            Action<int> onTransportProcessExited)
        {
            _ = vtProcessor;
            _ = onTransportDataReceived;
            _ = onTransportProcessExited;
            return ValueTask.CompletedTask;
        }

        public void ResizePty(int columns, int rows, int widthPixels, int heightPixels)
        {
            _ = columns;
            _ = rows;
            _ = widthPixels;
            _ = heightPixels;
        }

        public void ResizeSession(int columns, int rows, int widthPixels, int heightPixels)
        {
            _ = columns;
            _ = rows;
            _ = widthPixels;
            _ = heightPixels;
        }
    }
}
