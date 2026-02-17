// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Services.Contracts - Terminal session orchestration abstraction.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Terminal.Services;

/// <summary>
/// Manages terminal session lifecycle for surface/PTY integration.
/// </summary>
public interface ITerminalSessionService
{
    /// <summary>Gets the attached terminal endpoint, if any.</summary>
    ITerminalEndpoint? Endpoint { get; }

    /// <summary>Gets the attached endpoint input capability, if available.</summary>
    ITerminalInputSink? InputSink { get; }

    /// <summary>Gets the attached endpoint selection capability, if available.</summary>
    ITerminalSelectionSource? SelectionSource { get; }

    /// <summary>Gets the attached endpoint mode capability, if available.</summary>
    ITerminalModeSource? ModeSource { get; }

    /// <summary>Gets the active terminal transport, if any.</summary>
    ITerminalTransport? Transport { get; }

    /// <summary>Whether a transport session is currently active and running.</summary>
    bool HasActiveTransport { get; }

    /// <summary>Gets the active PTY, if any.</summary>
    IPty? Pty { get; }

    /// <summary>Whether a PTY session is currently active.</summary>
    bool HasPty { get; }

    /// <summary>
    /// Attaches a terminal endpoint, replacing any existing attachment.
    /// </summary>
    void AttachEndpoint(ITerminalEndpoint endpoint);

    /// <summary>
    /// Detaches the current terminal endpoint.
    /// </summary>
    void DetachEndpoint();

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
        Action<string> onVtTitleChanged,
        IReadOnlyList<string>? arguments = null);

    /// <summary>
    /// Starts a transport-backed terminal session and wires event/callback handlers.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a transport session is already active.
    /// </exception>
    ValueTask StartSessionAsync(
        ITerminalTransportFactory transportFactory,
        ITerminalTransportOptions transportOptions,
        IVtProcessor? vtProcessor,
        Action<byte[], int> onTransportDataReceived,
        Action<int> onTransportProcessExited,
        Action<byte[]> onVtResponse,
        Action onVtBell,
        Action<string> onVtTitleChanged,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the active PTY session and unwires handlers.
    /// VT processor lifetime remains owned by the caller/control.
    /// </summary>
    void StopPty(
        IVtProcessor? vtProcessor,
        Action<byte[], int> onPtyDataReceived,
        Action<int> onPtyProcessExited);

    /// <summary>
    /// Stops the active transport session and unwires handlers.
    /// VT processor lifetime remains owned by the caller/control.
    /// </summary>
    ValueTask StopSessionAsync(
        IVtProcessor? vtProcessor,
        Action<byte[], int> onTransportDataReceived,
        Action<int> onTransportProcessExited);

    /// <summary>
    /// Applies PTY size updates when a standalone PTY is active.
    /// </summary>
    void ResizePty(int columns, int rows, int widthPixels, int heightPixels);

    /// <summary>
    /// Applies session size updates for the active transport, if any.
    /// </summary>
    void ResizeSession(int columns, int rows, int widthPixels, int heightPixels);
}
