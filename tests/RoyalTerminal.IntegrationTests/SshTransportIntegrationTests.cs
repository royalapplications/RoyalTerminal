// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.IntegrationTests - SSH transport integration tests (opt-in via env vars).

using System.Globalization;
using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class SshTransportIntegrationTests
{
    private const string HostEnvVar = "ROYALTERMINAL_IT_SSH_HOST";
    private const string PortEnvVar = "ROYALTERMINAL_IT_SSH_PORT";
    private const string UsernameEnvVar = "ROYALTERMINAL_IT_SSH_USERNAME";
    private const string PasswordEnvVar = "ROYALTERMINAL_IT_SSH_PASSWORD";
    private const string PrivateKeyEnvVar = "ROYALTERMINAL_IT_SSH_PRIVATE_KEY";
    private const string FingerprintEnvVar = "ROYALTERMINAL_IT_SSH_HOST_KEY_SHA256";
    private const string RequestPtyEnvVar = "ROYALTERMINAL_IT_SSH_REQUEST_PTY";
    private const string TerminalTypeEnvVar = "ROYALTERMINAL_IT_SSH_TERMINAL_TYPE";

    [Fact]
    public async Task StartAsync_WithConfiguredServer_StreamsOutput()
    {
        if (!TryCreateConfiguredSetup(out IntegrationSetup setup))
        {
            return;
        }

        using SshNetTerminalTransport transport = new(
            credentialProvider: setup.CredentialProvider,
            hostKeyValidator: new RejectAllSshHostKeyValidator());

        StringBuilder output = new();
        TaskCompletionSource<bool> markerReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            string chunk = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains(setup.Marker, StringComparison.Ordinal))
                {
                    markerReceived.TrySetResult(true);
                }
            }
        };

        try
        {
            await transport.StartAsync(setup.Options);

            byte[] input = Encoding.UTF8.GetBytes($"echo {setup.Marker}\r\n");
            transport.SendInput(input);

            _ = await markerReceived.Task.WaitAsync(TimeSpan.FromSeconds(20));

            string rendered;
            lock (output)
            {
                rendered = output.ToString();
            }

            Assert.Contains(setup.Marker, rendered, StringComparison.Ordinal);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_WithMismatchedHostKeyPin_Fails()
    {
        if (!TryCreateConfiguredSetup(out IntegrationSetup setup))
        {
            return;
        }

        SshTransportOptions mismatchedOptions = setup.Options with
        {
            ExpectedHostKeyFingerprintSha256 = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        };

        using SshNetTerminalTransport transport = new(
            credentialProvider: setup.CredentialProvider,
            hostKeyValidator: new RejectAllSshHostKeyValidator());

        Exception? exception = await Record.ExceptionAsync(async () => await transport.StartAsync(mismatchedOptions));

        Assert.NotNull(exception);
        Assert.Contains("host key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task StartAsync_WithEnvironmentBootstrap_InitialCommandCanReadExportedVariable()
    {
        if (!TryCreateConfiguredSetup(out IntegrationSetup setup))
        {
            return;
        }

        string envMarker = "RT_SSH_ENV_" + Guid.NewGuid().ToString("N");
        SshTransportOptions options = setup.Options with
        {
            InitialCommand = "printf '%s\\n' \"$ROYALTERMINAL_ENV_PROBE\"",
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ROYALTERMINAL_ENV_PROBE"] = envMarker,
            },
        };

        using SshNetTerminalTransport transport = new(
            credentialProvider: setup.CredentialProvider,
            hostKeyValidator: new RejectAllSshHostKeyValidator());

        StringBuilder output = new();
        TaskCompletionSource<bool> markerReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            string chunk = Encoding.UTF8.GetString(data, 0, length);
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains(envMarker, StringComparison.Ordinal))
                {
                    markerReceived.TrySetResult(true);
                }
            }
        };

        try
        {
            await transport.StartAsync(options);
            _ = await markerReceived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    private static bool TryCreateConfiguredSetup(out IntegrationSetup setup)
    {
        string? host = Environment.GetEnvironmentVariable(HostEnvVar);
        string? username = Environment.GetEnvironmentVariable(UsernameEnvVar);
        string? fingerprint = Environment.GetEnvironmentVariable(FingerprintEnvVar);
        string? password = Environment.GetEnvironmentVariable(PasswordEnvVar);
        string? privateKey = Environment.GetEnvironmentVariable(PrivateKeyEnvVar);

        setup = default;

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        bool hasPassword = !string.IsNullOrWhiteSpace(password);
        bool hasPrivateKey = !string.IsNullOrWhiteSpace(privateKey);
        if (!hasPassword && !hasPrivateKey)
        {
            return false;
        }

        int port = ParsePortOrDefault(Environment.GetEnvironmentVariable(PortEnvVar), 22);
        bool requestPty = ParseBoolOrDefault(Environment.GetEnvironmentVariable(RequestPtyEnvVar), defaultValue: true);
        string? terminalTypeOverride = Environment.GetEnvironmentVariable(TerminalTypeEnvVar);
        string terminalType = string.IsNullOrWhiteSpace(terminalTypeOverride)
            ? "xterm-256color"
            : terminalTypeOverride;

        Dictionary<string, string> secrets = new(StringComparer.Ordinal);
        string? passwordSecretId = null;
        if (hasPassword)
        {
            passwordSecretId = "integration/password";
            secrets[passwordSecretId] = password!;
        }

        List<string> keySecretIds = new();
        if (hasPrivateKey)
        {
            string keySecretId = "integration/key/0";
            secrets[keySecretId] = privateKey!;
            keySecretIds.Add(keySecretId);
        }

        SshAuthenticationOptions authentication = new(
            UsePassword: hasPassword,
            PasswordSecretId: passwordSecretId,
            PrivateKeySecretIds: keySecretIds,
            UseAgent: false);

        string marker = "RT_SSH_IT_" + Guid.NewGuid().ToString("N");
        SshTransportOptions options = new(
            Endpoint: new SshEndpointOptions(host, port, username),
            RequestPty: requestPty,
            TerminalType: terminalType,
            InitialCommand: null,
            Authentication: authentication,
            Dimensions: new TerminalSessionDimensions(120, 40, 960, 720))
        {
            ExpectedHostKeyFingerprintSha256 = fingerprint,
        };

        setup = new IntegrationSetup(
            Options: options,
            Marker: marker,
            CredentialProvider: new SecretStoreSshCredentialProvider(new InMemorySshSecretStore(secrets)));
        return true;
    }

    private static int ParsePortOrDefault(string? rawValue, int defaultValue)
    {
        if (!string.IsNullOrWhiteSpace(rawValue) &&
            int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return defaultValue;
    }

    private static bool ParseBoolOrDefault(string? rawValue, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (bool.TryParse(rawValue, out bool parsed))
        {
            return parsed;
        }

        if (string.Equals(rawValue, "1", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(rawValue, "0", StringComparison.Ordinal))
        {
            return false;
        }

        return defaultValue;
    }

    private readonly record struct IntegrationSetup(
        SshTransportOptions Options,
        string Marker,
        ISshCredentialProvider CredentialProvider);
}
