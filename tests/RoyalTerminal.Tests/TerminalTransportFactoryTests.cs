// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalTransportFactoryTests
{
    [Fact]
    public void CompositeFactory_ResolvesKnownProviders_ByTransportId()
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory: new DefaultPtyFactory()),
                new PipeTerminalTransportProvider(),
                new SshNetTerminalTransportProvider(
                    credentialProvider: new NullSshCredentialProvider(),
                    hostKeyValidator: new RejectAllSshHostKeyValidator()),
            });

        TerminalSessionDimensions dimensions = new(80, 24, 640, 480);

        using ITerminalTransport ptyTransport = factory.Create(
            new PtyTransportOptions(
                Command: null,
                WorkingDirectory: null,
                Environment: null,
                Dimensions: dimensions));
        using ITerminalTransport pipeTransport = factory.Create(
            new PipeTransportOptions(
                Command: new TerminalCommandSpec("echo", Array.Empty<string>()),
                WorkingDirectory: null,
                Environment: null,
                MergeStdErrIntoStdOut: false,
                Dimensions: dimensions));
        using ITerminalTransport sshTransport = factory.Create(
            new SshTransportOptions(
                Endpoint: new SshEndpointOptions("localhost", 22, "user"),
                RequestPty: true,
                TerminalType: "xterm-256color",
                InitialCommand: null,
                Authentication: new SshAuthenticationOptions(
                    UsePassword: true,
                    PasswordSecretId: null,
                    PrivateKeySecretIds: Array.Empty<string>(),
                    UseAgent: false),
                Dimensions: dimensions));

        Assert.IsType<PtyTerminalTransport>(ptyTransport);
        Assert.IsType<PipeTerminalTransport>(pipeTransport);
        Assert.IsType<SshNetTerminalTransport>(sshTransport);
    }

    [Fact]
    public void CompositeFactory_ThrowsForUnknownTransportId()
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new PipeTerminalTransportProvider(),
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Create(new FakeTransportOptions("serial")));

        Assert.Contains("serial", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeFactory_ThrowsWhenProviderRejectsOptions()
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new RejectingProvider("pipe"),
            });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.Create(
                new PipeTransportOptions(
                    Command: new TerminalCommandSpec("echo", Array.Empty<string>()),
                    WorkingDirectory: null,
                    Environment: null,
                    MergeStdErrIntoStdOut: true,
                    Dimensions: new TerminalSessionDimensions(80, 24, 640, 480))));

        Assert.Contains("pipe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeFactory_RequiresAtLeastOneProvider()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new CompositeTerminalTransportFactory(Array.Empty<ITerminalTransportProvider>()));

        Assert.Contains("At least one transport provider", exception.Message, StringComparison.Ordinal);
    }

    private sealed record FakeTransportOptions(string TransportId) : ITerminalTransportOptions
    {
        public TerminalSessionDimensions Dimensions => new(80, 24, 640, 480);
    }

    private sealed class RejectingProvider : ITerminalTransportProvider
    {
        public RejectingProvider(string transportId)
        {
            TransportId = transportId;
        }

        public string TransportId { get; }

        public bool CanHandle(ITerminalTransportOptions options)
        {
            _ = options;
            return false;
        }

        public ITerminalTransport Create()
        {
            throw new NotSupportedException("Provider cannot create transport when options are rejected.");
        }
    }
}
