// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SshShellBootstrapCommandBuilderTests
{
    [Fact]
    public void BuildBootstrapCommand_ReturnsNull_WhenNoEnvironmentAndNoInitialCommand()
    {
        SshTransportOptions options = CreateOptions(initialCommand: null) with
        {
            EnvironmentVariables = null,
        };

        string? command = SshShellBootstrapCommandBuilder.Build(options);

        Assert.Null(command);
    }

    [Fact]
    public void BuildBootstrapCommand_ExportsEnvironment_AndAppendsInitialCommand()
    {
        SshTransportOptions options = CreateOptions(initialCommand: "echo ready") with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LANG"] = "en_US.UTF-8",
                ["LC_CTYPE"] = "en_US.UTF-8",
            },
        };

        string? command = SshShellBootstrapCommandBuilder.Build(options);

        Assert.NotNull(command);
        Assert.Contains("export LANG='en_US.UTF-8'", command, StringComparison.Ordinal);
        Assert.Contains("export LC_CTYPE='en_US.UTF-8'", command, StringComparison.Ordinal);
        Assert.EndsWith("echo ready", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBootstrapCommand_EscapesSingleQuotesInEnvironmentValues()
    {
        SshTransportOptions options = CreateOptions(initialCommand: null) with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ROYALTERMINAL_TOKEN"] = "ab'cd",
            },
        };

        string? command = SshShellBootstrapCommandBuilder.Build(options);

        Assert.NotNull(command);
        Assert.Equal("export ROYALTERMINAL_TOKEN='ab'\"'\"'cd'", command);
    }

    [Fact]
    public void BuildBootstrapCommand_ThrowsForInvalidEnvironmentName()
    {
        SshTransportOptions options = CreateOptions(initialCommand: null) with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BAD-NAME"] = "value",
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SshShellBootstrapCommandBuilder.Build(options));

        Assert.Contains("invalid identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBootstrapCommand_ThrowsForNewlineInEnvironmentValue()
    {
        SshTransportOptions options = CreateOptions(initialCommand: null) with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LANG"] = "en_US.UTF-8\nC.UTF-8",
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SshShellBootstrapCommandBuilder.Build(options));

        Assert.Contains("forbidden control characters", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBootstrapCommand_ThrowsForNullEnvironmentValue()
    {
        SshTransportOptions options = CreateOptions(initialCommand: null) with
        {
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LANG"] = null!,
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SshShellBootstrapCommandBuilder.Build(options));

        Assert.Contains("non-null value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_OverloadWithInitialCommandAndEnvironment_ProducesEquivalentBootstrap()
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["LANG"] = "en_US.UTF-8",
        };

        string? command = SshShellBootstrapCommandBuilder.Build("echo ready", environment);

        Assert.Equal("export LANG='en_US.UTF-8'; echo ready", command);
    }

    private static SshTransportOptions CreateOptions(string? initialCommand)
    {
        return new SshTransportOptions(
            Endpoint: new SshEndpointOptions("localhost", 22, "user"),
            RequestPty: true,
            TerminalType: "xterm-256color",
            InitialCommand: initialCommand,
            Authentication: new SshAuthenticationOptions(
                UsePassword: false,
                PasswordSecretId: null,
                PrivateKeySecretIds: Array.Empty<string>(),
                UseAgent: false),
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));
    }
}
