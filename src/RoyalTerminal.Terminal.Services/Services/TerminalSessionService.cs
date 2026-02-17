// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Services — Default terminal session service.

using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pty;

namespace RoyalTerminal.Terminal.Services;

/// <summary>
/// Default terminal session manager for endpoint-attached and standalone PTY modes.
/// </summary>
public sealed class TerminalSessionService : ITerminalSessionService
{
    private IVtProcessor? _activeVtProcessor;

    /// <inheritdoc />
    public ITerminalEndpoint? Endpoint { get; private set; }

    /// <inheritdoc />
    public ITerminalInputSink? InputSink { get; private set; }

    /// <inheritdoc />
    public ITerminalSelectionSource? SelectionSource { get; private set; }

    /// <inheritdoc />
    public ITerminalModeSource? ModeSource { get; private set; }

    /// <inheritdoc />
    public ITerminalTransport? Transport { get; private set; }

    /// <inheritdoc />
    public bool HasActiveTransport => Transport is { IsRunning: true };

    /// <inheritdoc />
    public IPty? Pty { get; private set; }

    /// <inheritdoc />
    public bool HasPty => Pty is not null;

    /// <inheritdoc />
    public void AttachEndpoint(ITerminalEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        DetachEndpoint();
        Endpoint = endpoint;
        InputSink = endpoint as ITerminalInputSink;
        SelectionSource = endpoint as ITerminalSelectionSource;
        ModeSource = endpoint as ITerminalModeSource;
    }

    /// <inheritdoc />
    public void DetachEndpoint()
    {
        Endpoint = null;
        InputSink = null;
        SelectionSource = null;
        ModeSource = null;
    }

    /// <inheritdoc />
    public void SendInput(string text)
    {
        ReleaseInactiveTransportIfNeeded();

        if (Endpoint is not null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Endpoint.SendText(Encoding.UTF8.GetBytes(text));
            }

            return;
        }

        if (Transport is not null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (Transport is ITerminalPtyTransport ptyTransport)
                {
                    ptyTransport.Pty.Write(text);
                }
                else
                {
                    Transport.SendInput(Encoding.UTF8.GetBytes(text));
                }
            }

            return;
        }

        Pty?.Write(text);
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> data)
    {
        ReleaseInactiveTransportIfNeeded();

        if (Endpoint is not null)
        {
            Endpoint.SendText(data);
            return;
        }

        if (Transport is not null)
        {
            Transport.SendInput(data);
            return;
        }

        if (Pty is null || data.IsEmpty)
        {
            return;
        }

        byte[] copy = data.ToArray();
        Pty.Write(copy, 0, copy.Length);
    }

    /// <inheritdoc />
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
        Action<string> onVtTitleChanged)
    {
        TerminalCommandSpec? command = string.IsNullOrWhiteSpace(shell)
            ? null
            : new TerminalCommandSpec(shell, Array.Empty<string>());
        TerminalSessionDimensions dimensions = new(columns, rows, WidthPixels: 0, HeightPixels: 0);
        PtyTransportOptions transportOptions = new(
            Command: command,
            WorkingDirectory: workingDirectory,
            Environment: null,
            Dimensions: dimensions);

        ITerminalTransportFactory transportFactory = new CompositeTerminalTransportFactory(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory: ptyFactory),
            });

        StartSessionAsync(
                transportFactory,
                transportOptions,
                vtProcessor,
                onPtyDataReceived,
                onPtyProcessExited,
                onVtResponse,
                onVtBell,
                onVtTitleChanged)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public async ValueTask StartSessionAsync(
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
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(transportOptions);
        cancellationToken.ThrowIfCancellationRequested();

        ReleaseInactiveTransportIfNeeded();

        if (Transport is not null)
        {
            throw new InvalidOperationException("A terminal transport session is already active.");
        }

        ITerminalTransport transport = transportFactory.Create(transportOptions);

        if (vtProcessor is not null)
        {
            // Configure callbacks before starting the transport to handle data
            // emitted synchronously during Start().
            vtProcessor.ResponseCallback = onVtResponse;
            vtProcessor.BellCallback = onVtBell;
            vtProcessor.TitleCallback = onVtTitleChanged;
        }

        _activeVtProcessor = vtProcessor;

        transport.DataReceived += onTransportDataReceived;
        transport.ProcessExited += onTransportProcessExited;

        try
        {
            await transport.StartAsync(transportOptions, cancellationToken).ConfigureAwait(false);
            Transport = transport;
            Pty = (transport as ITerminalPtyTransport)?.Pty;
        }
        catch
        {
            transport.DataReceived -= onTransportDataReceived;
            transport.ProcessExited -= onTransportProcessExited;
            if (vtProcessor is not null)
            {
                vtProcessor.ResponseCallback = null;
                vtProcessor.BellCallback = null;
                vtProcessor.TitleCallback = null;
            }

            _activeVtProcessor = null;

            transport.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void StopPty(
        IVtProcessor? vtProcessor,
        Action<byte[], int> onPtyDataReceived,
        Action<int> onPtyProcessExited)
    {
        StopSessionAsync(vtProcessor, onPtyDataReceived, onPtyProcessExited)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public async ValueTask StopSessionAsync(
        IVtProcessor? vtProcessor,
        Action<byte[], int> onTransportDataReceived,
        Action<int> onTransportProcessExited)
    {
        IVtProcessor? processorToClear = vtProcessor ?? _activeVtProcessor;

        if (processorToClear is not null)
        {
            processorToClear.ResponseCallback = null;
            processorToClear.BellCallback = null;
            processorToClear.TitleCallback = null;
        }

        _activeVtProcessor = null;

        ITerminalTransport? transport = Transport;
        if (transport is null)
        {
            return;
        }

        transport.DataReceived -= onTransportDataReceived;
        transport.ProcessExited -= onTransportProcessExited;

        try
        {
            await transport.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            transport.Dispose();
            Transport = null;
            Pty = null;
        }
    }

    /// <inheritdoc />
    public void ResizePty(int columns, int rows, int widthPixels, int heightPixels)
    {
        ResizeSession(columns, rows, widthPixels, heightPixels);
    }

    /// <inheritdoc />
    public void ResizeSession(int columns, int rows, int widthPixels, int heightPixels)
    {
        ReleaseInactiveTransportIfNeeded();

        if (Transport is null)
        {
            return;
        }

        Transport.Resize(new TerminalSessionDimensions(columns, rows, widthPixels, heightPixels));
    }

    private void ReleaseInactiveTransportIfNeeded()
    {
        ITerminalTransport? transport = Transport;
        if (transport is null || transport.IsRunning)
        {
            return;
        }

        if (_activeVtProcessor is not null)
        {
            _activeVtProcessor.ResponseCallback = null;
            _activeVtProcessor.BellCallback = null;
            _activeVtProcessor.TitleCallback = null;
            _activeVtProcessor = null;
        }

        try
        {
            transport.Dispose();
        }
        catch
        {
            // Best effort cleanup for stale transports.
        }

        Transport = null;
        Pty = null;
    }
}
