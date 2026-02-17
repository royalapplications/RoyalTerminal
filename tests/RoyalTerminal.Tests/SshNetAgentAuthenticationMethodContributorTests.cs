// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SshNetAgentAuthenticationMethodContributorTests
{
    [Fact]
    public void CreateAuthenticationMethods_WhenAgentNotRequested_ReturnsEmpty()
    {
        SshNetAgentAuthenticationMethodContributor contributor = new();
        SshTransportOptions options = CreateOptions(useAgent: false);
        SshResolvedCredentials credentials = new(
            Password: null,
            PrivateKeyPemOrPath: Array.Empty<string>(),
            UseAgent: false);

        IReadOnlyList<Renci.SshNet.AuthenticationMethod> methods =
            contributor.CreateAuthenticationMethods(options, credentials);

        Assert.Empty(methods);
    }

    [Fact]
    public void CreateAuthenticationMethods_WhenAgentRequested_DoesNotThrow()
    {
        SshNetAgentAuthenticationMethodContributor contributor = new();
        SshTransportOptions options = CreateOptions(useAgent: true);
        SshResolvedCredentials credentials = new(
            Password: null,
            PrivateKeyPemOrPath: Array.Empty<string>(),
            UseAgent: true);

        IReadOnlyList<Renci.SshNet.AuthenticationMethod> methods =
            contributor.CreateAuthenticationMethods(options, credentials);

        Assert.NotNull(methods);
    }

    private static SshTransportOptions CreateOptions(bool useAgent)
    {
        return new SshTransportOptions(
            Endpoint: new SshEndpointOptions("localhost", 22, "user"),
            RequestPty: true,
            TerminalType: "xterm-256color",
            InitialCommand: null,
            Authentication: new SshAuthenticationOptions(
                UsePassword: false,
                PasswordSecretId: null,
                PrivateKeySecretIds: Array.Empty<string>(),
                UseAgent: useAgent),
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));
    }
}
