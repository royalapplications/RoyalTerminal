// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Pty - PTY transport implementation.

using System.Text;

namespace RoyalTerminal.Terminal.Transport.Pty;

/// <summary>
/// Terminal transport that wraps an <see cref="IPty"/> instance.
/// </summary>
public sealed class PtyTerminalTransport : ITerminalPtyTransport
{
    private readonly IPtyFactory _ptyFactory;
    private readonly IShellProfileCatalog _shellProfileCatalog;
    private IPty? _pty;
    private bool _disposed;

    /// <summary>
    /// Initializes a new PTY transport.
    /// </summary>
    public PtyTerminalTransport(IPtyFactory ptyFactory, IShellProfileCatalog shellProfileCatalog)
    {
        _ptyFactory = ptyFactory ?? throw new ArgumentNullException(nameof(ptyFactory));
        _shellProfileCatalog = shellProfileCatalog ?? throw new ArgumentNullException(nameof(shellProfileCatalog));
    }

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <inheritdoc />
    public bool IsRunning => _pty?.IsRunning ?? false;

    /// <inheritdoc />
    public IPty Pty => _pty ?? throw new InvalidOperationException("PTY transport is not running.");

    /// <inheritdoc />
    public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pty is not null)
        {
            throw new InvalidOperationException("PTY transport is already running.");
        }

        if (options is not PtyTransportOptions ptyOptions)
        {
            throw new ArgumentException("Invalid options type for PTY transport.", nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();

        IPty pty = _ptyFactory.Create();
        pty.DataReceived += OnDataReceived;
        pty.ProcessExited += OnProcessExited;

        string? shellPath = ResolveShellPath(ptyOptions);
        Dictionary<string, string>? environment = ptyOptions.Environment is null
            ? null
            : new Dictionary<string, string>(ptyOptions.Environment);

        try
        {
            pty.Start(
                shell: shellPath,
                columns: Math.Max(1, ptyOptions.Dimensions.Columns),
                rows: Math.Max(1, ptyOptions.Dimensions.Rows),
                workingDirectory: ptyOptions.WorkingDirectory,
                environment: environment);
            _pty = pty;
        }
        catch
        {
            pty.DataReceived -= OnDataReceived;
            pty.ProcessExited -= OnProcessExited;
            pty.Dispose();
            throw;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (_pty is null || utf8.IsEmpty)
        {
            return;
        }

        byte[] copy = utf8.ToArray();
        _pty.Write(copy, 0, copy.Length);
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        _pty?.Resize(
            Math.Max(1, dimensions.Columns),
            Math.Max(1, dimensions.Rows),
            Math.Max(1, dimensions.WidthPixels),
            Math.Max(1, dimensions.HeightPixels));
    }

    /// <inheritdoc />
    public ValueTask StopAsync()
    {
        if (_pty is null)
        {
            return ValueTask.CompletedTask;
        }

        _pty.DataReceived -= OnDataReceived;
        _pty.ProcessExited -= OnProcessExited;
        _pty.Dispose();
        _pty = null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = StopAsync();
    }

    private string? ResolveShellPath(PtyTransportOptions options)
    {
        if (options.Command is { } command && !string.IsNullOrWhiteSpace(command.FileName))
        {
            // PTY backends currently accept shell executable path only.
            return command.FileName;
        }

        ShellProfile profile = _shellProfileCatalog.GetDefaultProfile();
        return profile.Command.FileName;
    }

    private void OnDataReceived(byte[] data, int length)
    {
        DataReceived?.Invoke(data, length);
    }

    private void OnProcessExited(int exitCode)
    {
        ProcessExited?.Invoke(exitCode);
    }
}

/// <summary>
/// Provider for PTY transport sessions.
/// </summary>
public sealed class PtyTerminalTransportProvider : ITerminalTransportProvider
{
    private readonly IPtyFactory _ptyFactory;
    private readonly IShellProfileCatalog _shellProfileCatalog;

    /// <summary>
    /// Creates a PTY transport provider.
    /// </summary>
    public PtyTerminalTransportProvider(IPtyFactory? ptyFactory = null, IShellProfileCatalog? shellProfileCatalog = null)
    {
        _ptyFactory = ptyFactory ?? new DefaultPtyFactory();
        _shellProfileCatalog = shellProfileCatalog ?? new DefaultShellProfileCatalog();
    }

    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Pty;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options)
    {
        return options is PtyTransportOptions;
    }

    /// <inheritdoc />
    public ITerminalTransport Create()
    {
        return new PtyTerminalTransport(_ptyFactory, _shellProfileCatalog);
    }
}
