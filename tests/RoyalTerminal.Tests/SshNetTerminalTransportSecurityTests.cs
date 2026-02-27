// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SshNetTerminalTransportSecurityTests
{
    [Fact]
    public async Task StartAsync_ThrowsForEmptyHost_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "   ", port: 22, username: "user");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task StartAsync_ThrowsForOutOfRangePort_BeforeCredentialResolution(int port)
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: port, username: "user");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("port", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForEmptyUsername_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: " ");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("username", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForInvalidEnvironmentVariableName_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BAD-NAME"] = "value",
            },
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("invalid identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForEnvironmentValueWithControlCharacters_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LANG"] = "en_US.UTF-8\nC.UTF-8",
            },
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("forbidden control characters", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForNullEnvironmentValue_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LANG"] = null!,
            },
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("non-null value", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForInvalidProxy_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            Proxy = new SshProxyOptions(
                Type: SshProxyType.Socks5,
                Host: " ",
                Port: 1080,
                Username: null,
                Password: null),
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("proxy host", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForInvalidPortForwarding_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            PortForwardings =
            [
                new SshPortForwardOptions(
                    Mode: SshPortForwardMode.Local,
                    BindAddress: "127.0.0.1",
                    SourcePort: 8080,
                    DestinationHost: "",
                    DestinationPort: 80),
            ],
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("DestinationHost", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForUnsupportedX11_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            X11 = new SshX11Options(
                Enabled: true,
                Display: ":0"),
        };

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("X11", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    [Fact]
    public async Task StartAsync_ThrowsForInvalidPolicy_BeforeCredentialResolution()
    {
        TrackingCredentialProvider credentialProvider = new();
        using SshNetTerminalTransport transport = new(
            credentialProvider,
            new RejectAllSshHostKeyValidator());

        SshTransportOptions options = CreateOptions(host: "localhost", port: 22, username: "user") with
        {
            Policy = new SshPolicyOptions(
                KeepAliveIntervalSeconds: 0,
                ConnectTimeoutSeconds: 10),
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.StartAsync(options));

        Assert.Contains("KeepAliveIntervalSeconds", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(credentialProvider.WasResolveCalled);
    }

    private static SshTransportOptions CreateOptions(string host, int port, string username)
    {
        return new SshTransportOptions(
            Endpoint: new SshEndpointOptions(host, port, username),
            RequestPty: true,
            TerminalType: "xterm-256color",
            InitialCommand: null,
            Authentication: new SshAuthenticationOptions(
                UsePassword: false,
                PasswordSecretId: null,
                PrivateKeySecretIds: Array.Empty<string>(),
                UseAgent: false),
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));
    }

    private sealed class TrackingCredentialProvider : ISshCredentialProvider
    {
        public bool WasResolveCalled { get; private set; }

        public ValueTask<SshResolvedCredentials> ResolveAsync(
            SshCredentialRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasResolveCalled = true;
            return ValueTask.FromResult(new SshResolvedCredentials(
                Password: null,
                PrivateKeyPemOrPath: Array.Empty<string>(),
                UseAgent: false));
        }
    }
}
