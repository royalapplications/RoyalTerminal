// Licensed under the MIT License.
// GhosttySharp.Tests — Tests for terminal abstractions introduced for decomposition.

using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Avalonia.Terminal;
using GhosttySharp.Terminal.Services;
using Xunit;

namespace GhosttySharp.Tests;

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
}
