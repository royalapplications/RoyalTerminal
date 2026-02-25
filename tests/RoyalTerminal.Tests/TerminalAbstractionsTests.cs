// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Tests for terminal abstractions introduced for decomposition.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalAbstractionsTests
{
    [Fact]
    public void DefaultVtProcessorFactory_ForcedManaged_ReturnsBasicProcessor()
    {
        TerminalScreen screen = new(80, 24, 10_000);
        DefaultVtProcessorFactory factory = new();

        IVtProcessor processor = factory.Create(screen, VtProcessorPreference.Managed);

        Assert.IsType<BasicVtProcessor>(processor);
        processor.Dispose();
    }

    [Fact]
    public void DefaultVtProcessorFactory_AutoMode_UsesNativeProvider_WhenAvailable()
    {
        TerminalScreen screen = new(80, 24, 10_000);
        FakeVtProcessor expected = new();
        FakeNativeVtProcessorProvider provider = new(expected, isAvailable: true);
        DefaultVtProcessorFactory factory = new([provider]);

        IVtProcessor processor = factory.Create(screen, VtProcessorPreference.Auto);

        Assert.Same(expected, processor);
        Assert.Equal(1, provider.CreateCallCount);
        processor.Dispose();
    }

    [Fact]
    public void DefaultVtProcessorFactory_AutoMode_FallsBackToManaged_WhenProviderThrows()
    {
        TerminalScreen screen = new(80, 24, 10_000);
        ThrowingNativeVtProcessorProvider provider = new(isAvailable: true);
        DefaultVtProcessorFactory factory = new([provider]);

        IVtProcessor processor = factory.Create(screen, VtProcessorPreference.Auto);

        Assert.IsType<BasicVtProcessor>(processor);
        Assert.Equal(1, provider.CreateCallCount);
        processor.Dispose();
    }

    [Fact]
    public void DefaultVtProcessorFactory_ForcedNative_Throws_WhenNoProviderAvailable()
    {
        TerminalScreen screen = new(80, 24, 10_000);
        FakeVtProcessor expected = new();
        FakeNativeVtProcessorProvider provider = new(expected, isAvailable: false);
        DefaultVtProcessorFactory factory = new([provider]);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => factory.Create(screen, VtProcessorPreference.Native));

        Assert.Contains("no native VT provider is available", ex.Message);
        Assert.Equal(0, provider.CreateCallCount);
        expected.Dispose();
    }

    [Fact]
    public void DefaultPtyFactory_ReturnsCurrentPlatformImplementation()
    {
        DefaultPtyFactory factory = new();
        IPty pty = factory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsPty>(pty);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.IsType<UnixPty>(pty);
        }
        else
        {
            throw new PlatformNotSupportedException("Test environment platform not expected.");
        }

        pty.Dispose();
    }

    [Fact]
    public void TerminalSessionService_StartAndStopPty_UsesFactoryAndLifecycle()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: "sh",
            columns: 120,
            rows: 40,
            workingDirectory: "/tmp",
            vtProcessor: null,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle);

        Assert.True(factory.CreateCalled);
        Assert.True(service.HasPty);
        Assert.Equal(fakePty, service.Pty);
        Assert.True(fakePty.StartCalled);
        Assert.Equal("sh", fakePty.StartShell);
        Assert.Equal(120, fakePty.StartColumns);
        Assert.Equal(40, fakePty.StartRows);
        Assert.Equal("/tmp", fakePty.StartWorkingDirectory);

        service.StopPty(vtProcessor: null, onPtyDataReceived: onData, onPtyProcessExited: onExit);

        Assert.False(service.HasPty);
        Assert.Null(service.Pty);
        Assert.True(fakePty.DisposeCalled);
    }

    [Fact]
    public void TerminalSessionService_StartPty_ForwardsCommandArguments()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: "sh",
            columns: 120,
            rows: 40,
            workingDirectory: "/tmp",
            vtProcessor: null,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle,
            arguments: ["-lc", "echo ready"]);

        Assert.Equal(["-lc", "echo ready"], fakePty.StartArguments);
        service.StopPty(vtProcessor: null, onPtyDataReceived: onData, onPtyProcessExited: onExit);
    }

    [Fact]
    public void TerminalSessionService_StartPty_ForwardsArguments_WhenShellIsAutoDetected()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: null,
            columns: 120,
            rows: 40,
            workingDirectory: "/tmp",
            vtProcessor: null,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle,
            arguments: ["-lc", "echo ready"]);

        Assert.False(string.IsNullOrWhiteSpace(fakePty.StartShell));
        Assert.Equal(["-lc", "echo ready"], fakePty.StartArguments);
        service.StopPty(vtProcessor: null, onPtyDataReceived: onData, onPtyProcessExited: onExit);
    }

    [Fact]
    public void TerminalSessionService_StopPty_DoesNotDisposeVtProcessor()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: "sh",
            columns: 120,
            rows: 40,
            workingDirectory: "/tmp",
            vtProcessor: vtProcessor,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle);

        Assert.NotNull(vtProcessor.ResponseCallback);
        Assert.NotNull(vtProcessor.BellCallback);
        Assert.NotNull(vtProcessor.TitleCallback);
        Assert.NotNull(service.ModeSource);
        Assert.Equal(vtProcessor.ModeState, service.ModeSource!.ModeState);

        service.StopPty(vtProcessor: vtProcessor, onPtyDataReceived: onData, onPtyProcessExited: onExit);

        Assert.False(vtProcessor.DisposeCalled);
        Assert.Null(vtProcessor.ResponseCallback);
        Assert.Null(vtProcessor.BellCallback);
        Assert.Null(vtProcessor.TitleCallback);
        Assert.Null(service.ModeSource);
    }

    [Fact]
    public void TerminalSessionService_StopThenStartPty_ReusesVtProcessorWithoutDisposal()
    {
        TerminalSessionService service = new();
        FakePty firstPty = new();
        FakePty secondPty = new();
        SequencePtyFactory factory = new(firstPty, secondPty);
        FakeVtProcessor vtProcessor = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: "sh",
            columns: 80,
            rows: 24,
            workingDirectory: "/tmp",
            vtProcessor: vtProcessor,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle);

        Assert.Equal(1, factory.CreateCallCount);
        Assert.True(firstPty.StartCalled);
        Assert.Same(firstPty, service.Pty);
        Assert.NotNull(vtProcessor.ResponseCallback);
        Assert.NotNull(vtProcessor.BellCallback);
        Assert.NotNull(vtProcessor.TitleCallback);

        service.StopPty(vtProcessor: vtProcessor, onPtyDataReceived: onData, onPtyProcessExited: onExit);

        Assert.True(firstPty.DisposeCalled);
        Assert.False(vtProcessor.DisposeCalled);
        Assert.Null(vtProcessor.ResponseCallback);
        Assert.Null(vtProcessor.BellCallback);
        Assert.Null(vtProcessor.TitleCallback);

        service.StartPty(
            factory,
            shell: "zsh",
            columns: 100,
            rows: 30,
            workingDirectory: "/var",
            vtProcessor: vtProcessor,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle);

        Assert.Equal(2, factory.CreateCallCount);
        Assert.True(secondPty.StartCalled);
        Assert.Equal("zsh", secondPty.StartShell);
        Assert.Equal(100, secondPty.StartColumns);
        Assert.Equal(30, secondPty.StartRows);
        Assert.Equal("/var", secondPty.StartWorkingDirectory);
        Assert.Same(secondPty, service.Pty);
        Assert.NotNull(vtProcessor.ResponseCallback);
        Assert.NotNull(vtProcessor.BellCallback);
        Assert.NotNull(vtProcessor.TitleCallback);

        service.StopPty(vtProcessor: vtProcessor, onPtyDataReceived: onData, onPtyProcessExited: onExit);

        Assert.True(secondPty.DisposeCalled);
        Assert.False(vtProcessor.DisposeCalled);
    }

    [Fact]
    public void TerminalSessionService_SendInput_UsesPty_WhenNoSurface()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: null,
            columns: 80,
            rows: 24,
            workingDirectory: null,
            vtProcessor: null,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle);

        service.SendInput("hello");
        service.SendInput("world"u8);

        Assert.Equal("hello", fakePty.LastWrittenText);
        Assert.Equal("world"u8.ToArray(), fakePty.LastWrittenBytes);
    }

    [Fact]
    public void TerminalSessionService_SendInput_UsesEndpoint_WhenAttached()
    {
        TerminalSessionService service = new();
        FakeTerminalEndpoint endpoint = new();
        service.AttachEndpoint(endpoint);

        service.SendInput("hello");
        service.SendInput("world"u8);

        Assert.Equal("world"u8.ToArray(), endpoint.LastUtf8Input);
    }

    [Fact]
    public void TerminalSessionService_DetachEndpoint_RoutesInputBackToPty()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);
        FakeTerminalEndpoint endpoint = new();

        Action<byte[], int> onData = (_, _) => { };
        Action<int> onExit = _ => { };
        Action<byte[]> onResponse = _ => { };
        Action onBell = () => { };
        Action<string> onTitle = _ => { };

        service.StartPty(
            factory,
            shell: null,
            columns: 80,
            rows: 24,
            workingDirectory: null,
            vtProcessor: null,
            onPtyDataReceived: onData,
            onPtyProcessExited: onExit,
            onVtResponse: onResponse,
            onVtBell: onBell,
            onVtTitleChanged: onTitle);

        service.AttachEndpoint(endpoint);
        service.SendInput("surface");
        Assert.Equal("surface", System.Text.Encoding.UTF8.GetString(endpoint.LastUtf8Input!));

        service.DetachEndpoint();
        service.SendInput("pty");
        Assert.Equal("pty", fakePty.LastWrittenText);
    }

    [Fact]
    public void TerminalSessionService_AttachEndpoint_ThrowsOnNull()
    {
        TerminalSessionService service = new();
        Assert.Throws<ArgumentNullException>(() => service.AttachEndpoint(null!));
    }

    [Fact]
    public void TerminalSessionService_StartPty_ReceivesEarlyDataEmittedDuringStart()
    {
        TerminalSessionService service = new();
        EarlyEmitFakePty fakePty = new("zsh% ");
        FakePtyFactory factory = new(fakePty);

        int receivedLength = 0;
        string? receivedText = null;

        void OnData(byte[] data, int length)
        {
            receivedLength = length;
            receivedText = System.Text.Encoding.UTF8.GetString(data, 0, length);
        }

        service.StartPty(
            factory,
            shell: "sh",
            columns: 80,
            rows: 24,
            workingDirectory: "/tmp",
            vtProcessor: null,
            onPtyDataReceived: OnData,
            onPtyProcessExited: _ => { },
            onVtResponse: _ => { },
            onVtBell: () => { },
            onVtTitleChanged: _ => { });

        Assert.True(service.HasPty);
        Assert.Equal(fakePty, service.Pty);
        Assert.Equal(fakePty.InitialPayload.Length, receivedLength);
        Assert.Equal(fakePty.InitialPayload, receivedText);
    }

    [Fact]
    public void TerminalSessionService_StartPty_ConfiguresVtCallbacksBeforeEarlyData()
    {
        TerminalSessionService service = new();
        EarlyEmitFakePty fakePty = new("\x1b[6n");
        FakePtyFactory factory = new(fakePty);
        FakeVtProcessor vtProcessor = new();
        bool callbacksConfiguredDuringEarlyData = false;

        void OnData(byte[] _data, int _length)
        {
            _ = _data;
            _ = _length;
            callbacksConfiguredDuringEarlyData =
                vtProcessor.ResponseCallback is not null
                && vtProcessor.BellCallback is not null
                && vtProcessor.TitleCallback is not null;
        }

        service.StartPty(
            factory,
            shell: "sh",
            columns: 80,
            rows: 24,
            workingDirectory: "/tmp",
            vtProcessor: vtProcessor,
            onPtyDataReceived: OnData,
            onPtyProcessExited: _ => { },
            onVtResponse: _ => { },
            onVtBell: () => { },
            onVtTitleChanged: _ => { });

        Assert.True(callbacksConfiguredDuringEarlyData);
    }

    private sealed class FakePtyFactory : IPtyFactory
    {
        private readonly IPty _pty;

        public FakePtyFactory(IPty pty)
        {
            _pty = pty;
        }

        public bool CreateCalled { get; private set; }

        public IPty Create()
        {
            CreateCalled = true;
            return _pty;
        }
    }

    private sealed class SequencePtyFactory : IPtyFactory
    {
        private readonly Queue<IPty> _ptys;

        public SequencePtyFactory(params IPty[] ptys)
        {
            _ptys = new Queue<IPty>(ptys);
        }

        public int CreateCallCount { get; private set; }

        public IPty Create()
        {
            CreateCallCount++;
            return _ptys.Dequeue();
        }
    }

    private sealed class FakePty : IPty
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;

        public bool StartCalled { get; private set; }
        public string? StartShell { get; private set; }
        public IReadOnlyList<string> StartArguments { get; private set; } = Array.Empty<string>();
        public int StartColumns { get; private set; }
        public int StartRows { get; private set; }
        public string? StartWorkingDirectory { get; private set; }
        public bool DisposeCalled { get; private set; }

        public bool IsRunning => StartCalled && !DisposeCalled;
        public int ChildPid => 1234;
        public string? LastWrittenText { get; private set; }
        public byte[]? LastWrittenBytes { get; private set; }

        public void Start(
            string? shell = null,
            int columns = 80,
            int rows = 24,
            string? workingDirectory = null,
            Dictionary<string, string>? environment = null,
            IReadOnlyList<string>? arguments = null)
        {
            _ = environment;
            StartCalled = true;
            StartShell = shell;
            StartArguments = arguments ?? Array.Empty<string>();
            StartColumns = columns;
            StartRows = rows;
            StartWorkingDirectory = workingDirectory;
        }

        public void Write(string text)
        {
            LastWrittenText = text;
        }

        public void Write(byte[] data, int offset, int count)
        {
            LastWrittenBytes = data.AsSpan(offset, count).ToArray();
            DataReceived?.Invoke(LastWrittenBytes, LastWrittenBytes.Length);
        }

        public void Resize(int columns, int rows)
        {
        }

        public void Resize(int columns, int rows, int widthPixels, int heightPixels)
        {
        }

        public void Stop()
        {
            Dispose();
        }

        public void Dispose()
        {
            DisposeCalled = true;
            ProcessExited?.Invoke(0);
        }
    }

    private sealed class EarlyEmitFakePty : IPty
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;

        public EarlyEmitFakePty(string initialPayload)
        {
            InitialPayload = initialPayload;
        }

        public string InitialPayload { get; }
        public bool IsRunning { get; private set; }
        public int ChildPid => 42;

        public void Start(
            string? shell = null,
            int columns = 80,
            int rows = 24,
            string? workingDirectory = null,
            Dictionary<string, string>? environment = null,
            IReadOnlyList<string>? arguments = null)
        {
            _ = shell;
            _ = columns;
            _ = rows;
            _ = workingDirectory;
            _ = environment;
            _ = arguments;

            IsRunning = true;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(InitialPayload);
            DataReceived?.Invoke(bytes, bytes.Length);
        }

        public void Write(string text)
        {
        }

        public void Write(byte[] data, int offset, int count)
        {
            _ = data;
            _ = offset;
            _ = count;
        }

        public void Resize(int columns, int rows)
        {
        }

        public void Resize(int columns, int rows, int widthPixels, int heightPixels)
        {
        }

        public void Stop()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            ProcessExited?.Invoke(0);
        }
    }

    private sealed class FakeTerminalEndpoint : ITerminalEndpoint
    {
        public byte[]? LastUtf8Input { get; private set; }
        public bool Focused { get; private set; }
        public int WidthPx { get; private set; }
        public int HeightPx { get; private set; }

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            LastUtf8Input = utf8.ToArray();
        }

        public void SetFocus(bool focused)
        {
            Focused = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            WidthPx = widthPx;
            HeightPx = heightPx;
        }
    }

    private sealed class FakeNativeVtProcessorProvider : INativeVtProcessorProvider
    {
        private readonly IVtProcessor _processor;

        public FakeNativeVtProcessorProvider(IVtProcessor processor, bool isAvailable)
        {
            _processor = processor;
            IsAvailable = isAvailable;
        }

        public bool IsAvailable { get; }
        public int CreateCallCount { get; private set; }

        public IVtProcessor Create(TerminalScreen screen)
        {
            _ = screen;
            CreateCallCount++;
            return _processor;
        }
    }

    private sealed class ThrowingNativeVtProcessorProvider : INativeVtProcessorProvider
    {
        public ThrowingNativeVtProcessorProvider(bool isAvailable)
        {
            IsAvailable = isAvailable;
        }

        public bool IsAvailable { get; }
        public int CreateCallCount { get; private set; }

        public IVtProcessor Create(TerminalScreen screen)
        {
            _ = screen;
            CreateCallCount++;
            throw new InvalidOperationException("Provider create failed.");
        }
    }

    private sealed class FakeVtProcessor : IVtProcessor
    {
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);
        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }
        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }
        public bool DisposeCalled { get; private set; }

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
            DisposeCalled = true;
        }
    }
}
