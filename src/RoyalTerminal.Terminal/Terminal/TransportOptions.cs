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

    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Ssh;
}
