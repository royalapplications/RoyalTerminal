// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.SshNet - SSH.NET transport implementation.

using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using RoyalTerminal.Terminal.Transport.Ssh;

namespace RoyalTerminal.Terminal.Transport.Ssh.SshNet;

/// <summary>
/// Terminal transport implemented over SSH.NET shell streams.
/// </summary>
public sealed class SshNetTerminalTransport : ITerminalTransport
{
    private readonly ISshCredentialProvider _credentialProvider;
    private readonly ISshHostKeyValidator _hostKeyValidator;
    private readonly IReadOnlyList<ISshNetAuthenticationMethodContributor> _authContributors;

    private readonly object _sync = new();
    private SshClient? _client;
    private ShellStream? _shellStream;
    private TransportWritePump? _writePump;
    private readonly List<ForwardedPort> _forwardedPorts = [];
    private SshTransportOptions? _options;
    private string? _lastHostKeyValidationError;
    private bool _disposed;
    private int _exitRaised;

    /// <summary>
    /// Initializes a new SSH.NET transport instance.
    /// </summary>
    public SshNetTerminalTransport(
        ISshCredentialProvider credentialProvider,
        ISshHostKeyValidator hostKeyValidator,
        IReadOnlyList<ISshNetAuthenticationMethodContributor>? authContributors = null)
    {
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _hostKeyValidator = hostKeyValidator ?? throw new ArgumentNullException(nameof(hostKeyValidator));
        _authContributors = authContributors ?? Array.Empty<ISshNetAuthenticationMethodContributor>();
    }

    /// <inheritdoc />
    public event Action<byte[], int>? DataReceived;

    /// <inheritdoc />
    public event Action<int>? ProcessExited;

    /// <inheritdoc />
    public bool IsRunning => _client is { IsConnected: true };

    /// <inheritdoc />
    public async ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (options is not SshTransportOptions sshOptions)
        {
            throw new ArgumentException("Invalid options type for SSH transport.", nameof(options));
        }

        ValidateOptions(sshOptions);

        lock (_sync)
        {
            if (_client is not null || _shellStream is not null)
            {
                throw new InvalidOperationException("SSH transport is already running.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        SshCredentialRequest credentialRequest = new(sshOptions.Endpoint, sshOptions.Authentication);
        SshResolvedCredentials credentials = await _credentialProvider
            .ResolveAsync(credentialRequest, cancellationToken)
            .ConfigureAwait(false);

        List<AuthenticationMethod> methods = BuildAuthenticationMethods(sshOptions, credentials);
        if (methods.Count == 0)
        {
            throw new InvalidOperationException("No SSH authentication methods are available for the session.");
        }

        ConnectionInfo connectionInfo = CreateConnectionInfo(sshOptions, methods);
        connectionInfo.Timeout = TimeSpan.FromSeconds(sshOptions.Policy.ConnectTimeoutSeconds);

        SshClient client = new(connectionInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(sshOptions.Policy.KeepAliveIntervalSeconds),
        };
        _lastHostKeyValidationError = null;
        client.HostKeyReceived += (_, e) => OnHostKeyReceived(sshOptions, e);

        try
        {
            await Task.Run(client.Connect, cancellationToken).ConfigureAwait(false);
            ConfigurePortForwardings(client, sshOptions);

            ShellStream shell = sshOptions.RequestPty
                ? client.CreateShellStream(
                    terminalName: string.IsNullOrWhiteSpace(sshOptions.TerminalType)
                        ? "xterm-256color"
                        : sshOptions.TerminalType,
                    columns: (uint)Math.Max(1, sshOptions.Dimensions.Columns),
                    rows: (uint)Math.Max(1, sshOptions.Dimensions.Rows),
                    width: (uint)Math.Max(1, sshOptions.Dimensions.WidthPixels),
                    height: (uint)Math.Max(1, sshOptions.Dimensions.HeightPixels),
                    bufferSize: 8192)
                : client.CreateShellStreamNoTerminal(8192);

            shell.DataReceived += OnShellDataReceived;
            shell.Closed += OnShellClosed;
            shell.ErrorOccurred += OnShellErrorOccurred;

            string? bootstrapCommand = SshShellBootstrapCommandBuilder.Build(sshOptions);
            if (!string.IsNullOrWhiteSpace(bootstrapCommand))
            {
                shell.WriteLine(bootstrapCommand);
            }

            TransportWritePump writePump = new(
                "RoyalTerminal.Ssh.Transport.Write",
                WriteInputDirect,
                OnWritePumpFaulted);

            lock (_sync)
            {
                _client = client;
                _shellStream = shell;
                _writePump = writePump;
                _options = sshOptions;
                _exitRaised = 0;
            }
        }
        catch (Exception ex)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }

            if (!string.IsNullOrWhiteSpace(_lastHostKeyValidationError))
            {
                throw new InvalidOperationException(_lastHostKeyValidationError, ex);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void SendInput(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return;
        }

        _writePump?.TryEnqueue(utf8);
    }

    /// <inheritdoc />
    public void Resize(TerminalSessionDimensions dimensions)
    {
        ShellStream? shell = _shellStream;
        SshTransportOptions? options = _options;

        if (shell is null || options is null || !options.RequestPty)
        {
            return;
        }

        shell.ChangeWindowSize(
            (uint)Math.Max(1, dimensions.Columns),
            (uint)Math.Max(1, dimensions.Rows),
            (uint)Math.Max(1, dimensions.WidthPixels),
            (uint)Math.Max(1, dimensions.HeightPixels));
    }

    /// <inheritdoc />
    public ValueTask StopAsync()
    {
        ShellStream? shell;
        SshClient? client;
        TransportWritePump? writePump;
        List<ForwardedPort> forwardedPorts;

        lock (_sync)
        {
            shell = _shellStream;
            client = _client;
            writePump = _writePump;
            forwardedPorts = new List<ForwardedPort>(_forwardedPorts);
            _shellStream = null;
            _client = null;
            _writePump = null;
            _options = null;
            _forwardedPorts.Clear();
        }

        if (shell is null && client is null && writePump is null && forwardedPorts.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        writePump?.RequestStop(discardPendingWrites: true);

        for (int i = 0; i < forwardedPorts.Count; i++)
        {
            try
            {
                forwardedPorts[i].Stop();
            }
            catch
            {
                // Best effort only.
            }

            if (client is not null)
            {
                try
                {
                    client.RemoveForwardedPort(forwardedPorts[i]);
                }
                catch
                {
                    // Best effort only.
                }
            }

            try
            {
                forwardedPorts[i].Dispose();
            }
            catch
            {
                // Best effort only.
            }
        }

        if (shell is not null)
        {
            shell.DataReceived -= OnShellDataReceived;
            shell.Closed -= OnShellClosed;
            shell.ErrorOccurred -= OnShellErrorOccurred;
            shell.Dispose();
        }

        if (client is not null)
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }

            client.Dispose();
        }

        _ = writePump?.Join(TimeSpan.FromSeconds(5));
        RaiseProcessExitedOnce(0);
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

    private List<AuthenticationMethod> BuildAuthenticationMethods(
        SshTransportOptions options,
        SshResolvedCredentials credentials)
    {
        List<AuthenticationMethod> methods = new();

        if (options.Authentication.UsePassword)
        {
            if (string.IsNullOrEmpty(credentials.Password))
            {
                throw new InvalidOperationException("Password authentication was requested but no password was resolved.");
            }

            methods.Add(new PasswordAuthenticationMethod(options.Endpoint.Username, credentials.Password));
        }

        List<IPrivateKeySource> keySources = new();
        for (int i = 0; i < credentials.PrivateKeyPemOrPath.Count; i++)
        {
            string keySource = credentials.PrivateKeyPemOrPath[i];
            if (string.IsNullOrWhiteSpace(keySource))
            {
                continue;
            }

            keySources.Add(CreatePrivateKeySource(keySource));
        }

        if (keySources.Count > 0)
        {
            methods.Add(new PrivateKeyAuthenticationMethod(options.Endpoint.Username, keySources.ToArray()));
        }

        for (int i = 0; i < _authContributors.Count; i++)
        {
            IReadOnlyList<AuthenticationMethod> extra = _authContributors[i].CreateAuthenticationMethods(options, credentials);
            for (int j = 0; j < extra.Count; j++)
            {
                methods.Add(extra[j]);
            }
        }

        return methods;
    }

    private static ConnectionInfo CreateConnectionInfo(
        SshTransportOptions options,
        List<AuthenticationMethod> methods)
    {
        SshProxyOptions? proxy = options.Proxy;
        if (proxy is not null && proxy.Type != SshProxyType.None)
        {
            return new ConnectionInfo(
                options.Endpoint.Host,
                options.Endpoint.Port,
                options.Endpoint.Username,
                MapProxyType(proxy.Type),
                proxy.Host,
                proxy.Port,
                proxy.Username ?? string.Empty,
                proxy.Password ?? string.Empty,
                methods.ToArray());
        }

        return new ConnectionInfo(
            options.Endpoint.Host,
            options.Endpoint.Port,
            options.Endpoint.Username,
            methods.ToArray());
    }

    private void ConfigurePortForwardings(SshClient client, SshTransportOptions options)
    {
        if (options.PortForwardings.Count == 0)
        {
            return;
        }

        List<ForwardedPort> created = new(options.PortForwardings.Count);
        try
        {
            for (int i = 0; i < options.PortForwardings.Count; i++)
            {
                ForwardedPort forwardedPort = CreateForwardedPort(options.PortForwardings[i]);
                client.AddForwardedPort(forwardedPort);
                forwardedPort.Start();
                created.Add(forwardedPort);
            }

            lock (_sync)
            {
                _forwardedPorts.AddRange(created);
            }
        }
        catch
        {
            for (int i = 0; i < created.Count; i++)
            {
                try
                {
                    created[i].Stop();
                }
                catch
                {
                    // Best effort cleanup.
                }

                try
                {
                    client.RemoveForwardedPort(created[i]);
                }
                catch
                {
                    // Best effort cleanup.
                }

                try
                {
                    created[i].Dispose();
                }
                catch
                {
                    // Best effort cleanup.
                }
            }

            throw;
        }
    }

    private static ForwardedPort CreateForwardedPort(SshPortForwardOptions options)
    {
        return options.Mode switch
        {
            SshPortForwardMode.Local => new ForwardedPortLocal(
                options.BindAddress,
                options.SourcePort,
                options.DestinationHost ?? throw new InvalidOperationException("Local SSH port forwarding requires DestinationHost."),
                options.DestinationPort ?? throw new InvalidOperationException("Local SSH port forwarding requires DestinationPort.")),

            SshPortForwardMode.Remote => new ForwardedPortRemote(
                options.BindAddress,
                options.SourcePort,
                options.DestinationHost ?? throw new InvalidOperationException("Remote SSH port forwarding requires DestinationHost."),
                options.DestinationPort ?? throw new InvalidOperationException("Remote SSH port forwarding requires DestinationPort.")),

            SshPortForwardMode.Dynamic => new ForwardedPortDynamic(
                options.BindAddress,
                options.SourcePort),

            _ => throw new InvalidOperationException($"Unsupported SSH port forwarding mode '{options.Mode}'."),
        };
    }

    private static ProxyTypes MapProxyType(SshProxyType proxyType)
    {
        return proxyType switch
        {
            SshProxyType.Http => ProxyTypes.Http,
            SshProxyType.Socks4 => ProxyTypes.Socks4,
            SshProxyType.Socks5 => ProxyTypes.Socks5,
            _ => ProxyTypes.None,
        };
    }

    private static IPrivateKeySource CreatePrivateKeySource(string keySource)
    {
        if (File.Exists(keySource))
        {
            return new PrivateKeyFile(keySource);
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(keySource);
        MemoryStream stream = new(utf8);
        return new PrivateKeyFile(stream);
    }

    private void OnHostKeyReceived(SshTransportOptions options, HostKeyEventArgs eventArgs)
    {
        string fingerprint = eventArgs.FingerPrintSHA256;
        string normalizedFingerprint = NormalizeFingerprint(fingerprint);
        string displayFingerprint = string.IsNullOrWhiteSpace(fingerprint)
            ? "<missing>"
            : fingerprint;

        if (!string.IsNullOrWhiteSpace(options.ExpectedHostKeyFingerprintSha256))
        {
            bool trusted = string.Equals(
                NormalizeFingerprint(options.ExpectedHostKeyFingerprintSha256),
                normalizedFingerprint,
                StringComparison.Ordinal);
            eventArgs.CanTrust = trusted;
            if (!trusted)
            {
                _lastHostKeyValidationError =
                    $"SSH host key mismatch for {options.Endpoint.Username}@{options.Endpoint.Host}:{options.Endpoint.Port}. " +
                    $"Expected SHA256 fingerprint '{options.ExpectedHostKeyFingerprintSha256}', received '{displayFingerprint}'.";
            }

            return;
        }

        try
        {
            SshHostKeyInfo keyInfo = new(
                eventArgs.HostKeyName,
                displayFingerprint,
                eventArgs.FingerPrintMD5,
                eventArgs.KeyLength,
                HostKeyBase64: Convert.ToBase64String(eventArgs.HostKey));
            bool trustedByValidator = _hostKeyValidator.IsTrusted(options.Endpoint, keyInfo);
            eventArgs.CanTrust = trustedByValidator;
            if (!trustedByValidator)
            {
                _lastHostKeyValidationError =
                    $"SSH host key is not trusted for {options.Endpoint.Username}@{options.Endpoint.Host}:{options.Endpoint.Port}. " +
                    $"Received SHA256 fingerprint '{displayFingerprint}'.";
            }
        }
        catch (Exception ex)
        {
            eventArgs.CanTrust = false;
            _lastHostKeyValidationError =
                $"SSH host-key validator failed for {options.Endpoint.Username}@{options.Endpoint.Host}:{options.Endpoint.Port}. " +
                $"Validation was rejected. {ex.Message}";
        }
    }

    private void OnShellDataReceived(object? sender, ShellDataEventArgs e)
    {
        byte[] data = e.Data;
        DataReceived?.Invoke(data, data.Length);
    }

    private void OnShellClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RaiseProcessExitedOnce(-1);
    }

    private void OnShellErrorOccurred(object? sender, ExceptionEventArgs e)
    {
        _ = sender;
        _ = e;
        RaiseProcessExitedOnce(-1);
    }

    private void WriteInputDirect(byte[] payload)
    {
        lock (_sync)
        {
            ShellStream? shell = _shellStream;
            if (shell is null)
            {
                return;
            }

            shell.Write(payload, 0, payload.Length);
            shell.Flush();
        }
    }

    private void OnWritePumpFaulted(Exception exception)
    {
        _ = exception;
        RaiseProcessExitedOnce(-1);
        _ = Task.Run(async () => await StopAsync().ConfigureAwait(false));
    }

    private void RaiseProcessExitedOnce(int exitCode)
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) == 1)
        {
            return;
        }

        ProcessExited?.Invoke(exitCode);
    }

    private static void ValidateOptions(SshTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Endpoint);
        ArgumentNullException.ThrowIfNull(options.Authentication);

        if (string.IsNullOrWhiteSpace(options.Endpoint.Host))
        {
            throw new InvalidOperationException("SSH endpoint host must not be empty.");
        }

        if (options.Endpoint.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("SSH endpoint port must be in range 1-65535.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint.Username))
        {
            throw new InvalidOperationException("SSH endpoint username must not be empty.");
        }

        SshShellBootstrapCommandBuilder.ValidateEnvironmentVariables(options.EnvironmentVariables);
        ValidateProxyOptions(options.Proxy);
        ValidatePortForwardings(options.PortForwardings);
        ValidatePolicy(options.Policy);
        ValidateX11(options.X11);
    }

    private static void ValidateProxyOptions(SshProxyOptions? proxy)
    {
        if (proxy is null || proxy.Type == SshProxyType.None)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(proxy.Host))
        {
            throw new InvalidOperationException("SSH proxy host must not be empty when proxy is enabled.");
        }

        if (proxy.Port is < 1 or > 65_535)
        {
            throw new InvalidOperationException("SSH proxy port must be in range 1-65535.");
        }
    }

    private static void ValidatePortForwardings(IReadOnlyList<SshPortForwardOptions> forwardings)
    {
        for (int i = 0; i < forwardings.Count; i++)
        {
            SshPortForwardOptions forward = forwardings[i];
            if (string.IsNullOrWhiteSpace(forward.BindAddress))
            {
                throw new InvalidOperationException("SSH port forwarding BindAddress must not be empty.");
            }

            if (forward.SourcePort > 65_535)
            {
                throw new InvalidOperationException("SSH port forwarding SourcePort must be <= 65535.");
            }

            if (forward.Mode is SshPortForwardMode.Local or SshPortForwardMode.Remote)
            {
                if (string.IsNullOrWhiteSpace(forward.DestinationHost))
                {
                    throw new InvalidOperationException("SSH local/remote forwarding requires DestinationHost.");
                }

                if (forward.DestinationPort is null || forward.DestinationPort > 65_535 || forward.DestinationPort == 0)
                {
                    throw new InvalidOperationException("SSH local/remote forwarding requires a DestinationPort in range 1-65535.");
                }
            }
        }
    }

    private static void ValidatePolicy(SshPolicyOptions policy)
    {
        if (policy.KeepAliveIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("SSH KeepAliveIntervalSeconds must be greater than zero.");
        }

        if (policy.ConnectTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("SSH ConnectTimeoutSeconds must be greater than zero.");
        }
    }

    private static void ValidateX11(SshX11Options? x11)
    {
        if (x11 is null || !x11.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(x11.Display))
        {
            throw new InvalidOperationException("SSH X11 forwarding requires a display value.");
        }

        throw new NotSupportedException(
            "SSH X11 forwarding is configured but not yet supported by the SSH.NET transport backend.");
    }

    private static string NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("SHA256:".Length);
        }

        return normalized.TrimEnd('=').Trim();
    }
}

/// <summary>
/// Provider for SSH terminal sessions using SSH.NET.
/// </summary>
public sealed class SshNetTerminalTransportProvider : ITerminalTransportProvider
{
    private readonly ISshCredentialProvider _credentialProvider;
    private readonly ISshHostKeyValidator _hostKeyValidator;
    private readonly IReadOnlyList<ISshNetAuthenticationMethodContributor> _authContributors;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    public SshNetTerminalTransportProvider(
        ISshCredentialProvider? credentialProvider = null,
        ISshHostKeyValidator? hostKeyValidator = null,
        IReadOnlyList<ISshNetAuthenticationMethodContributor>? authContributors = null)
    {
        _credentialProvider = credentialProvider ?? new NullSshCredentialProvider();
        _hostKeyValidator = hostKeyValidator ?? new KnownHostsSshHostKeyValidator();
        _authContributors = authContributors ?? Array.Empty<ISshNetAuthenticationMethodContributor>();
    }

    /// <inheritdoc />
    public string TransportId => TerminalTransportIds.Ssh;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options)
    {
        return options is SshTransportOptions;
    }

    /// <inheritdoc />
    public ITerminalTransport Create()
    {
        return new SshNetTerminalTransport(_credentialProvider, _hostKeyValidator, _authContributors);
    }
}
