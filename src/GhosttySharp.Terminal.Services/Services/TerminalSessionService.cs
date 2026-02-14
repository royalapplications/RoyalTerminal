// Licensed under the MIT License.
// GhosttySharp.Terminal.Services — Default terminal session service.

using GhosttySharp.Avalonia.Terminal;
using GhosttySharp.Native;

namespace GhosttySharp.Terminal.Services;

/// <summary>
/// Default terminal session manager for Ghostty surface and standalone PTY modes.
/// </summary>
public sealed class TerminalSessionService : ITerminalSessionService
{
    /// <inheritdoc />
    public GhosttySurface? Surface { get; private set; }

    /// <inheritdoc />
    public IPty? Pty { get; private set; }

    /// <inheritdoc />
    public bool HasPty => Pty is not null;

    /// <inheritdoc />
    public void AttachSurface(GhosttySurface surface)
    {
        DetachSurface();
        Surface = surface;
    }

    /// <inheritdoc />
    public void DetachSurface()
    {
        Surface = null;
    }

    /// <inheritdoc />
    public void SendInput(string text)
    {
        if (Surface is not null)
        {
            Surface.SendText(text);
            return;
        }

        Pty?.Write(text);
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> data)
    {
        if (Surface is not null)
        {
            Surface.SendText(data);
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

        IPty pty = ptyFactory.Create();
        pty.Start(shell, columns, rows, workingDirectory);
        pty.DataReceived += onPtyDataReceived;
        pty.ProcessExited += onPtyProcessExited;
        Pty = pty;

        if (vtProcessor is not null)
        {
            vtProcessor.ResponseCallback = onVtResponse;
            vtProcessor.BellCallback = onVtBell;
            vtProcessor.TitleCallback = onVtTitleChanged;
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

        vtProcessor?.Dispose();
    }

    /// <inheritdoc />
    public void ResizePty(int columns, int rows, int widthPixels, int heightPixels)
    {
        Pty?.Resize(columns, rows, widthPixels, heightPixels);
    }
}
