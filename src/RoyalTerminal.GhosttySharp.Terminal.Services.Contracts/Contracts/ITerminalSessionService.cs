// Licensed under the MIT License.
// RoyalTerminal.GhosttySharp.Terminal.Services.Contracts - Terminal session orchestration abstraction.

using RoyalTerminal.Avalonia.Terminal;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp.Terminal.Services;

/// <summary>
/// Manages terminal session lifecycle for surface/PTY integration.
/// </summary>
public interface ITerminalSessionService
{
    /// <summary>Gets the attached Ghostty surface, if any.</summary>
    GhosttySurface? Surface { get; }

    /// <summary>Gets the active PTY, if any.</summary>
    IPty? Pty { get; }

    /// <summary>Whether a PTY session is currently active.</summary>
    bool HasPty { get; }

    /// <summary>
    /// Attaches a Ghostty surface, replacing any existing attachment.
    /// </summary>
    void AttachSurface(GhosttySurface surface);

    /// <summary>
    /// Detaches the current Ghostty surface.
    /// </summary>
    void DetachSurface();

    /// <summary>
    /// Sends UTF-16 text input to the active terminal endpoint.
    /// </summary>
    void SendInput(string text);

    /// <summary>
    /// Sends UTF-8 byte input to the active terminal endpoint.
    /// </summary>
    void SendInput(ReadOnlySpan<byte> data);

    /// <summary>
    /// Starts a standalone PTY session and wires event/callback handlers.
    /// </summary>
    void StartPty(
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
        Action<string> onVtTitleChanged);

    /// <summary>
    /// Stops the active PTY session and unwires handlers.
    /// </summary>
    void StopPty(
        IVtProcessor? vtProcessor,
        Action<byte[], int> onPtyDataReceived,
        Action<int> onPtyProcessExited);

    /// <summary>
    /// Applies PTY size updates when a standalone PTY is active.
    /// </summary>
    void ResizePty(int columns, int rows, int widthPixels, int heightPixels);
}
