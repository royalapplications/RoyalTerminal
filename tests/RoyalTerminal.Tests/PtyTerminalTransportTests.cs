// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pty;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class PtyTerminalTransportTests
{
    [Fact]
    public async Task StartAsync_UsesCommandFileName_WhenProvided()
    {
        FakePty pty = new();
        FakePtyFactory ptyFactory = new(pty);
        StaticShellProfileCatalog shellCatalog = new("/bin/default-shell");
        PtyTerminalTransport transport = new(ptyFactory, shellCatalog);

        await transport.StartAsync(
            new PtyTransportOptions(
                Command: new TerminalCommandSpec("/bin/custom-shell", ["--login", "-i"]),
                WorkingDirectory: "/tmp",
                Environment: new Dictionary<string, string> { ["A"] = "B" },
                Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800)));

        Assert.True(pty.StartCalled);
        Assert.Equal("/bin/custom-shell", pty.StartShell);
        Assert.Equal(["--login", "-i"], pty.StartArguments);
        Assert.Equal(120, pty.StartColumns);
        Assert.Equal(40, pty.StartRows);
        Assert.Equal("/tmp", pty.StartWorkingDirectory);

        await transport.StopAsync();
    }

    [Fact]
    public async Task StartAsync_UsesCatalogDefault_WhenCommandNotProvided()
    {
        FakePty pty = new();
        FakePtyFactory ptyFactory = new(pty);
        StaticShellProfileCatalog shellCatalog = new("/bin/default-shell", ["-l"]);
        PtyTerminalTransport transport = new(ptyFactory, shellCatalog);

        await transport.StartAsync(
            new PtyTransportOptions(
                Command: null,
                WorkingDirectory: null,
                Environment: null,
                Dimensions: new TerminalSessionDimensions(80, 24, 640, 480)));

        Assert.Equal("/bin/default-shell", pty.StartShell);
        Assert.Equal(["-l"], pty.StartArguments);

        await transport.StopAsync();
    }

    [Fact]
    public async Task StartAsync_UsesCatalogDefaultExecutable_WhenOnlyArgumentsAreProvided()
    {
        FakePty pty = new();
        FakePtyFactory ptyFactory = new(pty);
        StaticShellProfileCatalog shellCatalog = new("/bin/default-shell", ["-l"]);
        PtyTerminalTransport transport = new(ptyFactory, shellCatalog);

        await transport.StartAsync(
            new PtyTransportOptions(
                Command: new TerminalCommandSpec(string.Empty, ["-i", "-c", "echo ready"]),
                WorkingDirectory: null,
                Environment: null,
                Dimensions: new TerminalSessionDimensions(80, 24, 640, 480)));

        Assert.Equal("/bin/default-shell", pty.StartShell);
        Assert.Equal(["-i", "-c", "echo ready"], pty.StartArguments);

        await transport.StopAsync();
    }

    [Fact]
    public async Task SendInputAndResize_PropagateToPty()
    {
        FakePty pty = new();
        FakePtyFactory ptyFactory = new(pty);
        StaticShellProfileCatalog shellCatalog = new("/bin/default-shell");
        PtyTerminalTransport transport = new(ptyFactory, shellCatalog);

        await transport.StartAsync(
            new PtyTransportOptions(
                Command: null,
                WorkingDirectory: null,
                Environment: null,
                Dimensions: new TerminalSessionDimensions(80, 24, 640, 480)));

        transport.SendInput("hello"u8);
        transport.Resize(new TerminalSessionDimensions(90, 30, 900, 600));

        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(pty.LastBytes!));
        Assert.Equal(90, pty.LastResizeColumns);
        Assert.Equal(30, pty.LastResizeRows);
        Assert.Equal(900, pty.LastResizeWidth);
        Assert.Equal(600, pty.LastResizeHeight);

        await transport.StopAsync();
    }

    private sealed class FakePtyFactory : IPtyFactory
    {
        private readonly IPty _pty;

        public FakePtyFactory(IPty pty)
        {
            _pty = pty;
        }

        public IPty Create()
        {
            return _pty;
        }
    }

    private sealed class StaticShellProfileCatalog : IShellProfileCatalog
    {
        private readonly ShellProfile _default;

        public StaticShellProfileCatalog(string shellPath, IReadOnlyList<string>? arguments = null)
        {
            _default = new ShellProfile(
                "default",
                "Default",
                new TerminalCommandSpec(shellPath, arguments ?? Array.Empty<string>()));
        }

        public IReadOnlyList<ShellProfile> GetProfiles()
        {
            return new[] { _default };
        }

        public ShellProfile GetDefaultProfile()
        {
            return _default;
        }
    }

    private sealed class FakePty : IPty
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;

        public bool IsRunning { get; private set; }
        public int ChildPid => 1;

        public bool StartCalled { get; private set; }
        public string? StartShell { get; private set; }
        public IReadOnlyList<string> StartArguments { get; private set; } = Array.Empty<string>();
        public int StartColumns { get; private set; }
        public int StartRows { get; private set; }
        public string? StartWorkingDirectory { get; private set; }

        public byte[]? LastBytes { get; private set; }

        public int LastResizeColumns { get; private set; }
        public int LastResizeRows { get; private set; }
        public int LastResizeWidth { get; private set; }
        public int LastResizeHeight { get; private set; }

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
            IsRunning = true;
        }

        public void Write(string text)
        {
            LastBytes = System.Text.Encoding.UTF8.GetBytes(text);
            DataReceived?.Invoke(LastBytes, LastBytes.Length);
        }

        public void Write(byte[] data, int offset, int count)
        {
            LastBytes = data.AsSpan(offset, count).ToArray();
            DataReceived?.Invoke(LastBytes, LastBytes.Length);
        }

        public void Resize(int columns, int rows)
        {
            LastResizeColumns = columns;
            LastResizeRows = rows;
        }

        public void Resize(int columns, int rows, int widthPixels, int heightPixels)
        {
            LastResizeColumns = columns;
            LastResizeRows = rows;
            LastResizeWidth = widthPixels;
            LastResizeHeight = heightPixels;
        }

        public void Stop()
        {
            IsRunning = false;
            ProcessExited?.Invoke(0);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
