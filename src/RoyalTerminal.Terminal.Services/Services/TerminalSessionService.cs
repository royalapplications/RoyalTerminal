// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Services — Default terminal session service.

using System.Text;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Terminal.Services;

/// <summary>
/// Default terminal session manager for endpoint-attached and standalone PTY modes.
/// </summary>
public sealed class TerminalSessionService : ITerminalSessionService
{
    /// <inheritdoc />
    public ITerminalEndpoint? Endpoint { get; private set; }

    /// <inheritdoc />
    public ITerminalInputSink? InputSink { get; private set; }

    /// <inheritdoc />
    public ITerminalSelectionSource? SelectionSource { get; private set; }

    /// <inheritdoc />
    public ITerminalModeSource? ModeSource { get; private set; }

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
        if (Endpoint is not null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Endpoint.SendText(Encoding.UTF8.GetBytes(text));
            }

            return;
        }

        Pty?.Write(text);
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> data)
    {
        if (Endpoint is not null)
        {
            Endpoint.SendText(data);
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
        if (Pty is not null)
        {
            return;
        }

        if (vtProcessor is not null)
        {
            // Configure callbacks before starting the PTY to handle data
            // emitted synchronously during Start().
            vtProcessor.ResponseCallback = onVtResponse;
            vtProcessor.BellCallback = onVtBell;
            vtProcessor.TitleCallback = onVtTitleChanged;
        }

        IPty pty = ptyFactory.Create();
        pty.DataReceived += onPtyDataReceived;
        pty.ProcessExited += onPtyProcessExited;

        try
        {
            pty.Start(shell, columns, rows, workingDirectory);
            Pty = pty;
        }
        catch
        {
            pty.DataReceived -= onPtyDataReceived;
            pty.ProcessExited -= onPtyProcessExited;
            if (vtProcessor is not null)
            {
                vtProcessor.ResponseCallback = null;
                vtProcessor.BellCallback = null;
                vtProcessor.TitleCallback = null;
            }
            pty.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void StopPty(
        IVtProcessor? vtProcessor,
        Action<byte[], int> onPtyDataReceived,
        Action<int> onPtyProcessExited)
    {
        if (Pty is null)
        {
            return;
        }

        if (vtProcessor is not null)
        {
            vtProcessor.ResponseCallback = null;
            vtProcessor.BellCallback = null;
            vtProcessor.TitleCallback = null;
        }

        Pty.DataReceived -= onPtyDataReceived;
        Pty.ProcessExited -= onPtyProcessExited;
        Pty.Dispose();
        Pty = null;
    }

    /// <inheritdoc />
    public void ResizePty(int columns, int rows, int widthPixels, int heightPixels)
    {
        Pty?.Resize(columns, rows, widthPixels, heightPixels);
    }
}
