// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Tests for terminal abstractions introduced for decomposition.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Terminal;
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

        IVtProcessor processor = factory.Create(screen, useNativeVtProcessor: false);

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

        IVtProcessor processor = factory.Create(screen, useNativeVtProcessor: null);

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

        IVtProcessor processor = factory.Create(screen, useNativeVtProcessor: null);

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
            () => factory.Create(screen, useNativeVtProcessor: true));

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
    public void TerminalSessionService_SendInput_UsesSurface_WhenAttached()
    {
        TerminalSessionService service = new();
        FakeTerminalSurface surface = new();
        service.AttachSurface(surface);

        service.SendInput("hello");
        service.SendInput("world"u8);

        Assert.Equal("hello", surface.LastTextInput);
        Assert.Equal("world"u8.ToArray(), surface.LastByteInput);
    }

    [Fact]
    public void TerminalSessionService_DetachSurface_RoutesInputBackToPty()
    {
        TerminalSessionService service = new();
        FakePty fakePty = new();
        FakePtyFactory factory = new(fakePty);
        FakeTerminalSurface surface = new();

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

        service.AttachSurface(surface);
        service.SendInput("surface");
        Assert.Equal("surface", surface.LastTextInput);

        service.DetachSurface();
        service.SendInput("pty");
        Assert.Equal("pty", fakePty.LastWrittenText);
    }

    [Fact]
    public void TerminalSessionService_AttachSurface_ThrowsOnNull()
    {
        TerminalSessionService service = new();
        Assert.Throws<ArgumentNullException>(() => service.AttachSurface(null!));
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

    private sealed class FakePty : IPty
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;

        public bool StartCalled { get; private set; }
        public string? StartShell { get; private set; }
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
            Dictionary<string, string>? environment = null)
        {
            _ = environment;
            StartCalled = true;
            StartShell = shell;
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
            Dictionary<string, string>? environment = null)
        {
            _ = shell;
            _ = columns;
            _ = rows;
            _ = workingDirectory;
            _ = environment;

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

    private sealed class FakeTerminalSurface : ITerminalSurface
    {
        public object NativeHandle => this;

        public string? LastTextInput { get; private set; }
        public byte[]? LastByteInput { get; private set; }

        public void SendInput(string text)
        {
            LastTextInput = text;
        }

        public void SendInput(ReadOnlySpan<byte> data)
        {
            LastByteInput = data.ToArray();
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
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
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

        public void Dispose()
        {
        }
    }
}
