// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Standard terminal transport options.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Transport identifier constants.
/// </summary>
public static class TerminalTransportIds
{
    /// <summary>Local pseudo-terminal transport.</summary>
    public const string Pty = "pty";

    /// <summary>Process pipe transport.</summary>
    public const string Pipe = "pipe";

    /// <summary>SSH transport.</summary>
    public const string Ssh = "ssh";

    /// <summary>Raw TCP socket transport.</summary>
    public const string RawTcp = "raw-tcp";

    /// <summary>Telnet transport.</summary>
    public const string Telnet = "telnet";

    /// <summary>Serial line transport.</summary>
    public const string Serial = "serial";
}

/// <summary>
/// Command specification (executable path plus argument list).
/// </summary>
/// <param name="FileName">Executable path.</param>
/// <param name="Arguments">Command arguments.</param>
public readonly record struct TerminalCommandSpec(
    string FileName,
    IReadOnlyList<string> Arguments);

/// <summary>
/// Options for a local PTY transport.
/// </summary>
public sealed record PtyTransportOptions(
    TerminalCommandSpec? Command,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Pty;
}

/// <summary>
/// Options for a non-PTY process transport.
/// </summary>
public sealed record PipeTransportOptions(
    TerminalCommandSpec Command,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    bool MergeStdErrIntoStdOut,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Pipe;
}

/// <summary>
/// SSH endpoint descriptor.
/// </summary>
public sealed record SshEndpointOptions(
    string Host,
    int Port,
    string Username);

/// <summary>
/// Supported SSH proxy types.
/// </summary>
public enum SshProxyType
{
    None,
    Socks4,
    Socks5,
    Http,
}

/// <summary>
/// SSH proxy settings.
/// </summary>
public sealed record SshProxyOptions(
    SshProxyType Type,
    string Host,
    int Port,
    string? Username,
    string? Password);

/// <summary>
/// Supported SSH port forwarding modes.
/// </summary>
public enum SshPortForwardMode
{
    Local,
    Remote,
    Dynamic,
}

/// <summary>
/// SSH port forwarding rule.
/// </summary>
public sealed record SshPortForwardOptions(
    SshPortForwardMode Mode,
    string BindAddress,
    uint SourcePort,
    string? DestinationHost,
    uint? DestinationPort);

/// <summary>
/// SSH X11 forwarding options.
/// </summary>
public sealed record SshX11Options(
    bool Enabled,
    string Display);

/// <summary>
/// SSH policy options.
/// </summary>
public sealed record SshPolicyOptions(
    int KeepAliveIntervalSeconds,
    int ConnectTimeoutSeconds);

/// <summary>
/// Options for an SSH terminal transport.
/// </summary>
public sealed record SshTransportOptions(
    SshEndpointOptions Endpoint,
    bool RequestPty,
    string TerminalType,
    string? InitialCommand,
    SshAuthenticationOptions Authentication,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <summary>
    /// Optional environment variables to set in the remote shell session.
    /// Applied via POSIX-style <c>export</c> bootstrap commands.
    /// Keys must be valid shell variable identifiers.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    /// <summary>
    /// Optional expected SHA-256 host key fingerprint used for strict host-key pinning.
    /// Accepts values with or without <c>SHA256:</c> prefix.
    /// </summary>
    public string? ExpectedHostKeyFingerprintSha256 { get; init; }

    /// <summary>
    /// Optional SSH proxy settings.
    /// </summary>
    public SshProxyOptions? Proxy { get; init; }

    /// <summary>
    /// Optional SSH port forward rules.
    /// </summary>
    public IReadOnlyList<SshPortForwardOptions> PortForwardings { get; init; } = Array.Empty<SshPortForwardOptions>();

    /// <summary>
    /// Optional X11 forwarding settings.
    /// </summary>
    public SshX11Options? X11 { get; init; }

    /// <summary>
    /// SSH runtime policy settings.
    /// </summary>
    public SshPolicyOptions Policy { get; init; } = new(
        KeepAliveIntervalSeconds: 30,
        ConnectTimeoutSeconds: 15);

    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Ssh;
}

/// <summary>
/// Options for a raw TCP transport.
/// </summary>
public sealed record RawTcpTransportOptions(
    string Host,
    int Port,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.RawTcp;
}

/// <summary>
/// Options for a Telnet transport.
/// </summary>
public sealed record TelnetTransportOptions(
    string Host,
    int Port,
    string TerminalType,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <summary>
    /// Optional startup command sent after the connection is established.
    /// </summary>
    public string? InitialCommand { get; init; }

    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Telnet;
}

/// <summary>
/// Serial parity settings.
/// </summary>
public enum TerminalSerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

/// <summary>
/// Serial stop-bit settings.
/// </summary>
public enum TerminalSerialStopBits
{
    One,
    OnePointFive,
    Two,
}

/// <summary>
/// Serial handshake settings.
/// </summary>
public enum TerminalSerialHandshake
{
    None,
    XOnXOff,
    RequestToSend,
    RequestToSendXOnXOff,
}

/// <summary>
/// Options for a serial line transport.
/// </summary>
public sealed record SerialTransportOptions(
    string PortName,
    int BaudRate,
    int DataBits,
    TerminalSerialParity Parity,
    TerminalSerialStopBits StopBits,
    TerminalSerialHandshake Handshake,
    TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <summary>
    /// Newline token used by the backing serial stream.
    /// </summary>
    public string NewLine { get; init; } = "\n";

    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Serial;
}
