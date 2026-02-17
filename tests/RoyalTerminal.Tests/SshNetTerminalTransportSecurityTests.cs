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
