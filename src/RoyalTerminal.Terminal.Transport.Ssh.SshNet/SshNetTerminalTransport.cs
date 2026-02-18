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

        ConnectionInfo connectionInfo = new(
            sshOptions.Endpoint.Host,
            sshOptions.Endpoint.Port,
            sshOptions.Endpoint.Username,
            methods.ToArray());

        SshClient client = new(connectionInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        };
        _lastHostKeyValidationError = null;
        client.HostKeyReceived += (_, e) => OnHostKeyReceived(sshOptions, e);

        try
        {
            await Task.Run(client.Connect, cancellationToken).ConfigureAwait(false);

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

            lock (_sync)
            {
                _client = client;
                _shellStream = shell;
                _options = sshOptions;
                _exitRaised = 0;
            }

            if (!string.IsNullOrWhiteSpace(sshOptions.InitialCommand))
            {
                shell.WriteLine(sshOptions.InitialCommand);
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

        ShellStream? shell = _shellStream;
        if (shell is null)
        {
            return;
        }

        byte[] copy = utf8.ToArray();
        shell.Write(copy, 0, copy.Length);
        shell.Flush();
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

        lock (_sync)
        {
            shell = _shellStream;
            client = _client;
            _shellStream = null;
            _client = null;
            _options = null;
        }

        if (shell is null && client is null)
        {
            return ValueTask.CompletedTask;
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
