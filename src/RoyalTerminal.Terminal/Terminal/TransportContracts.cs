// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Terminal transport contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Options for a concrete terminal transport session.
/// </summary>
public interface ITerminalTransportOptions
{
    /// <summary>
    /// Gets the transport identifier used for provider resolution.
    /// </summary>
    string TransportId { get; }

    /// <summary>
    /// Gets initial session dimensions.
    /// </summary>
    TerminalSessionDimensions Dimensions { get; }
}

/// <summary>
/// Logical and pixel dimensions for a terminal session.
/// </summary>
/// <param name="Columns">Terminal columns.</param>
/// <param name="Rows">Terminal rows.</param>
/// <param name="WidthPixels">Terminal width in pixels.</param>
/// <param name="HeightPixels">Terminal height in pixels.</param>
public readonly record struct TerminalSessionDimensions(
    int Columns,
    int Rows,
    int WidthPixels,
    int HeightPixels);

/// <summary>
/// Runtime terminal transport abstraction.
/// </summary>
public interface ITerminalTransport : IDisposable
{
    /// <summary>Raised when output bytes are received from the transport.</summary>
    event Action<byte[], int>? DataReceived;

    /// <summary>Raised when the underlying process/session exits.</summary>
    event Action<int>? ProcessExited;

    /// <summary>Gets whether the transport is currently active.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the transport session.
    /// </summary>
    ValueTask StartAsync(
        ITerminalTransportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends UTF-8 input bytes to the transport.
    /// </summary>
    void SendInput(ReadOnlySpan<byte> utf8);

    /// <summary>
    /// Applies a terminal resize.
    /// </summary>
    void Resize(TerminalSessionDimensions dimensions);

    /// <summary>
    /// Stops the active transport.
    /// </summary>
    ValueTask StopAsync();
}

/// <summary>
/// Optional transport capability that exposes a backing PTY object.
/// </summary>
public interface ITerminalPtyTransport : ITerminalTransport
{
    /// <summary>
    /// Gets the backing PTY instance.
    /// </summary>
    IPty Pty { get; }
}

/// <summary>
/// Factory participant that can instantiate a concrete transport.
/// </summary>
public interface ITerminalTransportProvider
{
    /// <summary>
    /// Gets the provider transport identifier.
    /// </summary>
    string TransportId { get; }

    /// <summary>
    /// Determines whether this provider can handle the supplied options.
    /// </summary>
    bool CanHandle(ITerminalTransportOptions options);

    /// <summary>
    /// Creates a transport instance.
    /// </summary>
    ITerminalTransport Create();
}

/// <summary>
/// Creates transport instances for session options.
/// </summary>
public interface ITerminalTransportFactory
{
    /// <summary>
    /// Creates a transport for the supplied options.
    /// </summary>
    ITerminalTransport Create(ITerminalTransportOptions options);
}
