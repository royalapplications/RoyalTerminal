// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Session profile contracts and transport mapping helpers.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

internal static class TerminalSessionProfileDefaults
{
    public static readonly string DefaultMonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";
}

/// <summary>
/// Supported session log output formats in persisted profiles.
/// </summary>
public enum TerminalSessionLogFormat
{
    /// <summary>
    /// Human-readable text output.
    /// </summary>
    PlainText,

    /// <summary>
    /// Raw byte stream output.
    /// </summary>
    RawBytes,
}

/// <summary>
/// Font source used by terminal appearance settings.
/// </summary>
public enum TerminalFontSource
{
    /// <summary>
    /// Use an installed system font family.
    /// </summary>
    System,

    /// <summary>
    /// Load a font face from a font file on disk.
    /// </summary>
    File,
}

/// <summary>
/// Supported proxy profile types.
/// </summary>
public enum TerminalSessionProxyType
{
    /// <summary>
    /// No proxy.
    /// </summary>
    None,

    /// <summary>
    /// HTTP proxy.
    /// </summary>
    Http,

    /// <summary>
    /// SOCKS4 proxy.
    /// </summary>
    Socks4,

    /// <summary>
    /// SOCKS5 proxy.
    /// </summary>
    Socks5,

    /// <summary>
    /// Custom command proxy.
    /// </summary>
    Command,
}

/// <summary>
/// Versioned document payload for persisted session profiles.
/// </summary>
public sealed record TerminalSessionProfilesDocument
{
    /// <summary>
    /// Current supported profile document format version.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Profile document format version.
    /// </summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>
    /// Optional default profile identifier used when opening a new session.
    /// </summary>
    public string? DefaultProfileId { get; init; }

    /// <summary>
    /// Persisted profile entries.
    /// </summary>
    public List<TerminalSessionProfile> Profiles { get; init; } = [];
}

/// <summary>
/// Named session profile entry.
/// </summary>
public sealed record TerminalSessionProfile
{
    /// <summary>
    /// Stable profile identifier.
    /// </summary>
    public string Id { get; init; } = "default";

    /// <summary>
    /// Human-readable profile name.
    /// </summary>
    public string DisplayName { get; init; } = "Default";

    /// <summary>
    /// Terminal and viewport layout settings.
    /// </summary>
    public TerminalSessionLayoutSettings Layout { get; init; } = new();

    /// <summary>
    /// Appearance preferences.
    /// </summary>
    public TerminalSessionAppearanceSettings Appearance { get; init; } = new();

    /// <summary>
    /// Terminal interaction and behavior preferences.
    /// </summary>
    public TerminalSessionBehaviorSettings Behavior { get; init; } = new();

    /// <summary>
    /// Transport and endpoint settings.
    /// </summary>
    public TerminalSessionTransportProfile Transport { get; init; } = new();

    /// <summary>
    /// Session logging settings.
    /// </summary>
    public TerminalSessionLoggingSettings Logging { get; init; } = new();

    /// <summary>
    /// Proxy settings.
    /// </summary>
    public TerminalSessionProxySettings Proxy { get; init; } = new();
}

/// <summary>
/// Terminal layout settings persisted with a profile.
/// </summary>
public sealed record TerminalSessionLayoutSettings
{
    /// <summary>
    /// Terminal column count.
    /// </summary>
    public int Columns { get; init; } = 120;

    /// <summary>
    /// Terminal row count.
    /// </summary>
    public int Rows { get; init; } = 40;

    /// <summary>
    /// Viewport width in pixels.
    /// </summary>
    public int WidthPixels { get; init; } = 1200;

    /// <summary>
    /// Viewport height in pixels.
    /// </summary>
    public int HeightPixels { get; init; } = 800;

    /// <summary>
    /// Scrollback buffer size.
    /// </summary>
    public int ScrollbackLimit { get; init; } = 10_000;
}

/// <summary>
/// Appearance preferences for a terminal profile.
/// </summary>
public sealed record TerminalSessionAppearanceSettings
{
    /// <summary>
    /// Source used to resolve the terminal font.
    /// </summary>
    public TerminalFontSource FontSource { get; init; } = TerminalFontSource.System;

    /// <summary>
    /// Font family name.
    /// </summary>
    public string FontFamilyName { get; init; } = TerminalSessionProfileDefaults.DefaultMonoFont;

    /// <summary>
    /// Optional font file path used when <see cref="FontSource"/> is <see cref="TerminalFontSource.File"/>.
    /// </summary>
    public string? FontFilePath { get; init; }

    /// <summary>
    /// Font size in points.
    /// </summary>
    public double FontSize { get; init; } = 14.0;

    /// <summary>
    /// Whether the terminal auto-scrolls on incoming output.
    /// </summary>
    public bool AutoScroll { get; init; } = true;

    /// <summary>
    /// Whether background opacity rendering is enabled.
    /// </summary>
    public bool BackgroundOpacityEnabled { get; init; }
}

/// <summary>
/// Terminal interaction behavior preferences for a session profile.
/// </summary>
public sealed record TerminalSessionBehaviorSettings
{
    /// <summary>
    /// Whether selecting text copies it to the clipboard.
    /// </summary>
    public bool CopyOnSelectEnabled { get; init; }

    /// <summary>
    /// Whether bell notifications are enabled.
    /// </summary>
    public bool EnableBellNotifications { get; init; } = true;

    /// <summary>
    /// Whether Backspace sends Ctrl-H.
    /// </summary>
    public bool BackspaceSendsControlH { get; init; }

    /// <summary>
    /// Whether text shaping is enabled.
    /// </summary>
    public bool EnableTextShaping { get; init; } = true;

    /// <summary>
    /// Whether buffered terminal lines reflow when the terminal width changes.
    /// </summary>
    public bool ReflowOnResize { get; init; } = true;

    /// <summary>
    /// Whether managed VT sixel graphics decoding is enabled.
    /// </summary>
    public bool SixelGraphicsEnabled { get; init; } = true;

    /// <summary>
    /// Whether ligatures are enabled.
    /// </summary>
    public bool EnableLigatures { get; init; }

    /// <summary>
    /// Paste safety policy for clipboard pastes.
    /// </summary>
    public string PasteSafetyPolicy { get; init; } = "None";
}

/// <summary>
/// Transport settings envelope for a profile.
/// </summary>
public sealed record TerminalSessionTransportProfile
{
    /// <summary>
    /// Selected transport identifier.
    /// </summary>
    public string TransportId { get; init; } = TerminalTransportIds.Pty;

    /// <summary>
    /// PTY transport-specific settings.
    /// </summary>
    public TerminalSessionPtySettings Pty { get; init; } = new();

    /// <summary>
    /// Pipe transport-specific settings.
    /// </summary>
    public TerminalSessionPipeSettings Pipe { get; init; } = new();

    /// <summary>
    /// SSH transport-specific settings.
    /// </summary>
    public TerminalSessionSshSettings Ssh { get; init; } = new();

    /// <summary>
    /// Raw TCP transport-specific settings.
    /// </summary>
    public TerminalSessionRawTcpSettings RawTcp { get; init; } = new();

    /// <summary>
    /// Telnet transport-specific settings.
    /// </summary>
    public TerminalSessionTelnetSettings Telnet { get; init; } = new();

    /// <summary>
    /// Serial transport-specific settings.
    /// </summary>
    public TerminalSessionSerialSettings Serial { get; init; } = new();
}

/// <summary>
/// PTY transport settings.
/// </summary>
public sealed record TerminalSessionPtySettings
{
    /// <summary>
    /// Optional shell/program path. Empty means default shell.
    /// </summary>
    public string? ShellPath { get; init; }

    /// <summary>
    /// Optional shell/program arguments.
    /// </summary>
    public List<string> Arguments { get; init; } = [];

    /// <summary>
    /// Optional working directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Optional environment variables.
    /// </summary>
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Pipe transport settings.
/// </summary>
public sealed record TerminalSessionPipeSettings
{
    /// <summary>
    /// Command executable path.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Command arguments.
    /// </summary>
    public List<string> Arguments { get; init; } = [];

    /// <summary>
    /// Optional working directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Optional environment variables.
    /// </summary>
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether stderr is merged into stdout.
    /// </summary>
    public bool MergeStdErrIntoStdOut { get; init; } = true;
}

/// <summary>
/// SSH transport settings.
/// </summary>
public sealed record TerminalSessionSshSettings
{
    /// <summary>
    /// SSH host name or IP.
    /// </summary>
    public string Host { get; init; } = "localhost";

    /// <summary>
    /// SSH port number.
    /// </summary>
    public int Port { get; init; } = 22;

    /// <summary>
    /// SSH username.
    /// </summary>
    public string Username { get; init; } = System.Environment.UserName;

    /// <summary>
    /// Whether to request a pseudo-terminal.
    /// </summary>
    public bool RequestPty { get; init; } = true;

    /// <summary>
    /// Requested terminal type string.
    /// </summary>
    public string TerminalType { get; init; } = "xterm-256color";

    /// <summary>
    /// Optional initial command.
    /// </summary>
    public string? InitialCommand { get; init; }

    /// <summary>
    /// Optional expected SHA-256 host key fingerprint.
    /// </summary>
    public string? ExpectedHostKeyFingerprintSha256 { get; init; }

    /// <summary>
    /// Optional remote environment variables.
    /// </summary>
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// SSH authentication settings.
    /// </summary>
    public TerminalSessionSshAuthenticationSettings Authentication { get; init; } = new();

    /// <summary>
    /// Optional SSH proxy settings.
    /// </summary>
    public SshProxyOptions? Proxy { get; init; }

    /// <summary>
    /// SSH port forwarding rules.
    /// </summary>
    public List<SshPortForwardOptions> PortForwardings { get; init; } = [];

    /// <summary>
    /// Optional X11 forwarding settings.
    /// </summary>
    public SshX11Options? X11 { get; init; }

    /// <summary>
    /// SSH policy settings.
    /// </summary>
    public SshPolicyOptions Policy { get; init; } = new(
        KeepAliveIntervalSeconds: 30,
        ConnectTimeoutSeconds: 15);
}

/// <summary>
/// Raw TCP transport settings.
/// </summary>
public sealed record TerminalSessionRawTcpSettings
{
    /// <summary>
    /// Raw TCP host name or IP.
    /// </summary>
    public string Host { get; init; } = "localhost";

    /// <summary>
    /// Raw TCP port number.
    /// </summary>
    public int Port { get; init; } = 23;
}

/// <summary>
/// Telnet transport settings.
/// </summary>
public sealed record TerminalSessionTelnetSettings
{
    /// <summary>
    /// Telnet host name or IP.
    /// </summary>
    public string Host { get; init; } = "localhost";

    /// <summary>
    /// Telnet port number.
    /// </summary>
    public int Port { get; init; } = 23;

    /// <summary>
    /// Terminal type string.
    /// </summary>
    public string TerminalType { get; init; } = "xterm";

    /// <summary>
    /// Optional startup command.
    /// </summary>
    public string? InitialCommand { get; init; }
}

/// <summary>
/// Serial transport settings.
/// </summary>
public sealed record TerminalSessionSerialSettings
{
    /// <summary>
    /// Serial port name.
    /// </summary>
    public string PortName { get; init; } = string.Empty;

    /// <summary>
    /// Serial baud rate.
    /// </summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>
    /// Number of data bits.
    /// </summary>
    public int DataBits { get; init; } = 8;

    /// <summary>
    /// Serial parity mode.
    /// </summary>
    public TerminalSerialParity Parity { get; init; } = TerminalSerialParity.None;

    /// <summary>
    /// Serial stop-bit mode.
    /// </summary>
    public TerminalSerialStopBits StopBits { get; init; } = TerminalSerialStopBits.One;

    /// <summary>
    /// Serial handshake mode.
    /// </summary>
    public TerminalSerialHandshake Handshake { get; init; } = TerminalSerialHandshake.None;

    /// <summary>
    /// Serial newline token.
    /// </summary>
    public string NewLine { get; init; } = "\n";
}

/// <summary>
/// SSH authentication settings.
/// </summary>
public sealed record TerminalSessionSshAuthenticationSettings
{
    /// <summary>
    /// Whether password authentication is enabled.
    /// </summary>
    public bool UsePassword { get; init; }

    /// <summary>
    /// Optional password secret identifier.
    /// </summary>
    public string? PasswordSecretId { get; init; }

    /// <summary>
    /// Private key secret identifiers.
    /// </summary>
    public List<string> PrivateKeySecretIds { get; init; } = [];

    /// <summary>
    /// Whether agent authentication is enabled.
    /// </summary>
    public bool UseAgent { get; init; }
}

/// <summary>
/// Session logging settings.
/// </summary>
public sealed record TerminalSessionLoggingSettings
{
    /// <summary>
    /// Whether session logging is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Optional log file path.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Session log format.
    /// </summary>
    public TerminalSessionLogFormat Format { get; init; } = TerminalSessionLogFormat.PlainText;

    /// <summary>
    /// Whether writes should be flushed frequently.
    /// </summary>
    public bool FlushFrequently { get; init; }

    /// <summary>
    /// Whether the in-app event log surface is enabled.
    /// </summary>
    public bool EventLogEnabled { get; init; } = true;
}

/// <summary>
/// Proxy settings.
/// </summary>
public sealed record TerminalSessionProxySettings
{
    /// <summary>
    /// Whether proxying is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Proxy type.
    /// </summary>
    public TerminalSessionProxyType Type { get; init; } = TerminalSessionProxyType.None;

    /// <summary>
    /// Proxy host.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// Proxy port.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Proxy username.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Optional proxy password secret identifier.
    /// </summary>
    public string? PasswordSecretId { get; init; }

    /// <summary>
    /// Optional custom proxy command.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Excluded hosts/patterns.
    /// </summary>
    public List<string> ExcludedHosts { get; init; } = [];
}

/// <summary>
/// Converts persisted session profiles to runtime transport options and back.
/// </summary>
public static class TerminalSessionProfileMapper
{
    /// <summary>
    /// Converts a profile entry into transport options.
    /// </summary>
    public static ITerminalTransportOptions ToTransportOptions(TerminalSessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string transportId = NormalizeTransportId(profile.Transport.TransportId);
        TerminalSessionDimensions dimensions = CreateDimensions(profile.Layout);

        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.Ordinal))
        {
            string? shellPath = NormalizeOptional(profile.Transport.Pty.ShellPath);
            IReadOnlyList<string> arguments = ToReadOnlyList(profile.Transport.Pty.Arguments);
            TerminalCommandSpec? command = null;
            if (shellPath is not null || arguments.Count > 0)
            {
                command = new TerminalCommandSpec(shellPath ?? string.Empty, arguments);
            }

            return new PtyTransportOptions(
                Command: command,
                WorkingDirectory: NormalizeOptional(profile.Transport.Pty.WorkingDirectory),
                Environment: ToReadOnlyDictionary(profile.Transport.Pty.Environment),
                Dimensions: dimensions);
        }

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.Ordinal))
        {
            string fileName = NormalizeRequired(
                profile.Transport.Pipe.FileName,
                "Pipe transport requires a non-empty FileName.");

            return new PipeTransportOptions(
                Command: new TerminalCommandSpec(fileName, ToReadOnlyList(profile.Transport.Pipe.Arguments)),
                WorkingDirectory: NormalizeOptional(profile.Transport.Pipe.WorkingDirectory),
                Environment: ToReadOnlyDictionary(profile.Transport.Pipe.Environment),
                MergeStdErrIntoStdOut: profile.Transport.Pipe.MergeStdErrIntoStdOut,
                Dimensions: dimensions);
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.Ordinal))
        {
            TerminalSessionSshSettings ssh = profile.Transport.Ssh;
            int port = ssh.Port is > 0 and <= 65_535
                ? ssh.Port
                : throw new InvalidOperationException("SSH profile port must be between 1 and 65535.");

            string host = NormalizeRequired(ssh.Host, "SSH profile host is required.");
            string username = NormalizeRequired(ssh.Username, "SSH profile username is required.");
            TerminalSessionSshAuthenticationSettings auth = ssh.Authentication;

            SshTransportOptions options = new(
                Endpoint: new SshEndpointOptions(host, port, username),
                RequestPty: ssh.RequestPty,
                TerminalType: NormalizeOptional(ssh.TerminalType) ?? "xterm-256color",
                InitialCommand: NormalizeOptional(ssh.InitialCommand),
                Authentication: new SshAuthenticationOptions(
                    UsePassword: auth.UsePassword,
                    PasswordSecretId: NormalizeOptional(auth.PasswordSecretId),
                    PrivateKeySecretIds: ToReadOnlyList(auth.PrivateKeySecretIds),
                    UseAgent: auth.UseAgent),
                Dimensions: dimensions)
            {
                EnvironmentVariables = ToReadOnlyDictionary(ssh.Environment),
                ExpectedHostKeyFingerprintSha256 = NormalizeOptional(ssh.ExpectedHostKeyFingerprintSha256),
                Proxy = ssh.Proxy,
                PortForwardings = ToReadOnlyPortForwardings(ssh.PortForwardings),
                X11 = ssh.X11,
                Policy = ssh.Policy,
            };

            return options;
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.Ordinal))
        {
            TerminalSessionRawTcpSettings raw = profile.Transport.RawTcp;
            string host = NormalizeRequired(raw.Host, "Raw TCP profile host is required.");
            int port = raw.Port is > 0 and <= 65_535
                ? raw.Port
                : throw new InvalidOperationException("Raw TCP profile port must be between 1 and 65535.");

            return new RawTcpTransportOptions(host, port, dimensions);
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.Ordinal))
        {
            TerminalSessionTelnetSettings telnet = profile.Transport.Telnet;
            string host = NormalizeRequired(telnet.Host, "Telnet profile host is required.");
            int port = telnet.Port is > 0 and <= 65_535
                ? telnet.Port
                : throw new InvalidOperationException("Telnet profile port must be between 1 and 65535.");

            return new TelnetTransportOptions(
                Host: host,
                Port: port,
                TerminalType: NormalizeOptional(telnet.TerminalType) ?? "xterm",
                Dimensions: dimensions)
            {
                InitialCommand = NormalizeOptional(telnet.InitialCommand),
            };
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.Ordinal))
        {
            TerminalSessionSerialSettings serial = profile.Transport.Serial;
            string portName = NormalizeRequired(serial.PortName, "Serial profile port name is required.");
            if (serial.BaudRate <= 0)
            {
                throw new InvalidOperationException("Serial profile baud rate must be greater than zero.");
            }

            if (serial.DataBits is < 5 or > 8)
            {
                throw new InvalidOperationException("Serial profile data bits must be in range 5-8.");
            }

            return new SerialTransportOptions(
                PortName: portName,
                BaudRate: serial.BaudRate,
                DataBits: serial.DataBits,
                Parity: serial.Parity,
                StopBits: serial.StopBits,
                Handshake: serial.Handshake,
                Dimensions: dimensions)
            {
                NewLine = serial.NewLine,
            };
        }

        throw new NotSupportedException($"Unsupported transport id '{transportId}'.");
    }

    /// <summary>
    /// Creates a persisted profile from runtime transport options.
    /// </summary>
    public static TerminalSessionProfile FromTransportOptions(
        string id,
        string displayName,
        ITerminalTransportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(options);

        string normalizedId = id.Trim();
        string normalizedDisplayName = displayName.Trim();

        return options switch
        {
            PtyTransportOptions ptyOptions => new TerminalSessionProfile
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                Layout = FromDimensions(ptyOptions.Dimensions),
                Transport = new TerminalSessionTransportProfile
                {
                    TransportId = TerminalTransportIds.Pty,
                    Pty = new TerminalSessionPtySettings
                    {
                        ShellPath = NormalizeOptional(ptyOptions.Command?.FileName),
                        Arguments = ToMutableList(ptyOptions.Command?.Arguments),
                        WorkingDirectory = NormalizeOptional(ptyOptions.WorkingDirectory),
                        Environment = ToMutableDictionary(ptyOptions.Environment),
                    },
                },
            },

            PipeTransportOptions pipeOptions => new TerminalSessionProfile
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                Layout = FromDimensions(pipeOptions.Dimensions),
                Transport = new TerminalSessionTransportProfile
                {
                    TransportId = TerminalTransportIds.Pipe,
                    Pipe = new TerminalSessionPipeSettings
                    {
                        FileName = pipeOptions.Command.FileName,
                        Arguments = ToMutableList(pipeOptions.Command.Arguments),
                        WorkingDirectory = NormalizeOptional(pipeOptions.WorkingDirectory),
                        Environment = ToMutableDictionary(pipeOptions.Environment),
                        MergeStdErrIntoStdOut = pipeOptions.MergeStdErrIntoStdOut,
                    },
                },
            },

            SshTransportOptions sshOptions => new TerminalSessionProfile
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                Layout = FromDimensions(sshOptions.Dimensions),
                Transport = new TerminalSessionTransportProfile
                {
                    TransportId = TerminalTransportIds.Ssh,
                    Ssh = new TerminalSessionSshSettings
                    {
                        Host = sshOptions.Endpoint.Host,
                        Port = sshOptions.Endpoint.Port,
                        Username = sshOptions.Endpoint.Username,
                        RequestPty = sshOptions.RequestPty,
                        TerminalType = sshOptions.TerminalType,
                        InitialCommand = NormalizeOptional(sshOptions.InitialCommand),
                        ExpectedHostKeyFingerprintSha256 = NormalizeOptional(sshOptions.ExpectedHostKeyFingerprintSha256),
                        Environment = ToMutableDictionary(sshOptions.EnvironmentVariables),
                        Proxy = sshOptions.Proxy,
                        PortForwardings = ToMutablePortForwardings(sshOptions.PortForwardings),
                        X11 = sshOptions.X11,
                        Policy = sshOptions.Policy,
                        Authentication = new TerminalSessionSshAuthenticationSettings
                        {
                            UsePassword = sshOptions.Authentication.UsePassword,
                            PasswordSecretId = NormalizeOptional(sshOptions.Authentication.PasswordSecretId),
                            PrivateKeySecretIds = ToMutableList(sshOptions.Authentication.PrivateKeySecretIds),
                            UseAgent = sshOptions.Authentication.UseAgent,
                        },
                    },
                },
            },

            RawTcpTransportOptions rawOptions => new TerminalSessionProfile
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                Layout = FromDimensions(rawOptions.Dimensions),
                Transport = new TerminalSessionTransportProfile
                {
                    TransportId = TerminalTransportIds.RawTcp,
                    RawTcp = new TerminalSessionRawTcpSettings
                    {
                        Host = rawOptions.Host,
                        Port = rawOptions.Port,
                    },
                },
            },

            TelnetTransportOptions telnetOptions => new TerminalSessionProfile
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                Layout = FromDimensions(telnetOptions.Dimensions),
                Transport = new TerminalSessionTransportProfile
                {
                    TransportId = TerminalTransportIds.Telnet,
                    Telnet = new TerminalSessionTelnetSettings
                    {
                        Host = telnetOptions.Host,
                        Port = telnetOptions.Port,
                        TerminalType = telnetOptions.TerminalType,
                        InitialCommand = NormalizeOptional(telnetOptions.InitialCommand),
                    },
                },
            },

            SerialTransportOptions serialOptions => new TerminalSessionProfile
            {
                Id = normalizedId,
                DisplayName = normalizedDisplayName,
                Layout = FromDimensions(serialOptions.Dimensions),
                Transport = new TerminalSessionTransportProfile
                {
                    TransportId = TerminalTransportIds.Serial,
                    Serial = new TerminalSessionSerialSettings
                    {
                        PortName = serialOptions.PortName,
                        BaudRate = serialOptions.BaudRate,
                        DataBits = serialOptions.DataBits,
                        Parity = serialOptions.Parity,
                        StopBits = serialOptions.StopBits,
                        Handshake = serialOptions.Handshake,
                        NewLine = serialOptions.NewLine,
                    },
                },
            },

            _ => throw new NotSupportedException(
                $"Unsupported transport options type '{options.GetType().FullName}'."),
        };
    }

    private static TerminalSessionDimensions CreateDimensions(TerminalSessionLayoutSettings layout)
    {
        return new TerminalSessionDimensions(
            Math.Max(1, layout.Columns),
            Math.Max(1, layout.Rows),
            Math.Max(1, layout.WidthPixels),
            Math.Max(1, layout.HeightPixels));
    }

    private static TerminalSessionLayoutSettings FromDimensions(TerminalSessionDimensions dimensions)
    {
        return new TerminalSessionLayoutSettings
        {
            Columns = dimensions.Columns,
            Rows = dimensions.Rows,
            WidthPixels = dimensions.WidthPixels,
            HeightPixels = dimensions.HeightPixels,
        };
    }

    private static IReadOnlyDictionary<string, string>? ToReadOnlyDictionary(Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = pair.Value;
        }

        return normalized.Count > 0 ? normalized : null;
    }

    private static Dictionary<string, string> ToMutableDictionary(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        Dictionary<string, string> copy = new(values.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            copy[pair.Key.Trim()] = pair.Value;
        }

        return copy;
    }

    private static IReadOnlyList<string> ToReadOnlyList(List<string> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> copy = new(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            copy.Add(values[i]);
        }

        return copy;
    }

    private static List<string> ToMutableList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        List<string> copy = new(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            copy.Add(values[i]);
        }

        return copy;
    }

    private static IReadOnlyList<SshPortForwardOptions> ToReadOnlyPortForwardings(List<SshPortForwardOptions> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<SshPortForwardOptions>();
        }

        List<SshPortForwardOptions> copy = new(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            copy.Add(values[i]);
        }

        return copy;
    }

    private static List<SshPortForwardOptions> ToMutablePortForwardings(IReadOnlyList<SshPortForwardOptions>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        List<SshPortForwardOptions> copy = new(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            copy.Add(values[i]);
        }

        return copy;
    }

    private static string NormalizeRequired(string? value, string message)
    {
        string? normalized = NormalizeOptional(value);
        return normalized ?? throw new InvalidOperationException(message);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeTransportId(string? transportId)
    {
        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pty;
        }

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pipe;
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Ssh;
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.RawTcp;
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Telnet;
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Serial;
        }

        throw new NotSupportedException($"Unsupported transport id '{transportId}'.");
    }
}
