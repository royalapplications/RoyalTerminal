// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class HostCorrectnessTests
{
    [Fact]
    public async Task RevocationBetweenResolutionAndRegistrationPreventsStreaming()
    {
        await using Fixture fixture = new();
        MultiplexerSessionDescriptor session = fixture.Broker.CreateSession("shared");
        MultiplexerBlockDescriptor block = await fixture.CreateBlockAsync(session.Id);
        ShareCreated share = fixture.Tokens.Create(session.Id, new("observer"));
        AccessGrant previouslyResolved = Assert.IsType<AccessGrant>(fixture.Tokens.Resolve(share.Token));

        // This is the exact race ordering: authorization has completed, then revoke
        // removes the token and visits the registry before the socket is registered.
        Assert.True(fixture.Tokens.Revoke(session.Id, share.Id));
        fixture.Clients.Revoke(share.Id);
        using MultiplexerAttachment attachment = fixture.Broker.Attach(session.Id, block.Id, MultiplexerRole.Observer);
        using CancellationTokenSource lifetime = new();

        ApiException error = Assert.Throws<ApiException>(() =>
            fixture.Streams.RegisterAuthorizedClient(attachment, previouslyResolved, lifetime));

        Assert.Equal(403, error.Status);
        Assert.Equal("expired", error.Code);
        Assert.Empty(fixture.Clients.List(fixture.Administrator).Clients);
    }

    [Fact]
    public async Task RevocationAfterRegistrationCancelsTheLiveClient()
    {
        await using Fixture fixture = new();
        MultiplexerSessionDescriptor session = fixture.Broker.CreateSession("shared");
        MultiplexerBlockDescriptor block = await fixture.CreateBlockAsync(session.Id);
        ShareCreated share = fixture.Tokens.Create(session.Id, new("observer"));
        AccessGrant grant = Assert.IsType<AccessGrant>(fixture.Tokens.Resolve(share.Token));
        using MultiplexerAttachment attachment = fixture.Broker.Attach(session.Id, block.Id, MultiplexerRole.Observer);
        using CancellationTokenSource lifetime = new();
        fixture.Streams.RegisterAuthorizedClient(attachment, grant, lifetime);

        Assert.Single(fixture.Clients.List(fixture.Administrator).Clients);
        Assert.True(fixture.Tokens.Revoke(session.Id, share.Id));
        fixture.Clients.Revoke(share.Id);

        Assert.True(lifetime.IsCancellationRequested);
        fixture.Clients.Remove(attachment.Id);
    }

    [Fact]
    public async Task RejectedCloseCannotRemoveAnotherSessionsInspection()
    {
        await using Fixture fixture = new();
        MultiplexerSessionDescriptor first = fixture.Broker.CreateSession("first");
        MultiplexerSessionDescriptor second = fixture.Broker.CreateSession("second");
        MultiplexerBlockDescriptor block = await fixture.CreateBlockAsync(second.Id);
        InspectingTerminalTransport inspection = fixture.Workspaces.Inspection(second.Id, block.Id);
        string originalCapture = inspection.Capture(TerminalSnapshotExportFormat.PlainText);
        Assert.Contains("retained-output", originalCapture, StringComparison.Ordinal);

        MultiplexerException error = await Assert.ThrowsAsync<MultiplexerException>(() =>
            fixture.Workspaces.CloseBlockAsync(first.Id, block.Id).AsTask());

        Assert.Equal(MultiplexerErrorCode.NotFound, error.Code);
        Assert.Same(inspection, fixture.Workspaces.Inspection(second.Id, block.Id));
        Assert.Equal(originalCapture, fixture.Workspaces.Inspection(second.Id, block.Id)
            .Capture(TerminalSnapshotExportFormat.PlainText));
        Assert.Equal(MultiplexerBlockState.Running, fixture.Workspaces.GetBlock(second.Id, block.Id).State);

        await fixture.Workspaces.CloseBlockAsync(second.Id, block.Id);
        Assert.Throws<MultiplexerException>(() => fixture.Workspaces.Inspection(second.Id, block.Id));
    }

    [Theory]
    [InlineData("pty")]
    [InlineData("pipe")]
    public async Task ChildEnvironmentExplicitlyBlanksTheHostAdministratorCapability(string transport)
    {
        await using Fixture fixture = new();
        MultiplexerSessionDescriptor session = fixture.Broker.CreateSession("environment");
        await fixture.CreateBlockAsync(session.Id, transport);

        IReadOnlyDictionary<string, string>? environment = fixture.Factory.LastOptions switch
        {
            PtyTransportOptions options => options.Environment,
            PipeTransportOptions options => options.Environment,
            _ => throw new InvalidOperationException("Expected a local process transport."),
        };

        // Both local backends merge parent variables with these explicit overrides.
        // An empty value is supported on Unix/Windows and does not change global env.
        Assert.NotNull(environment);
        Assert.Equal(string.Empty, environment["ROYALTERMINAL_MUX_TOKEN"]);
        Assert.Equal(session.Id.ToString(), environment["ROYALTERMINAL_MUX_SESSION"]);
        Assert.Equal("xterm-256color", environment["TERM"]);
    }

    [Theory]
    [InlineData("\"sixty\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("false")]
    public void WrongNumericJsonTypesAreClassifiedAsBadRequests(string value)
    {
        using JsonDocument document = JsonDocument.Parse("{\"value\":" + value + ",\"expectedRevision\":" + value + "}");
        ApiArguments arguments = new(document.RootElement);

        AssertBadRequest(() => arguments.Integer("value", 1));
        AssertBadRequest(() => arguments.Number("value", 0.5));
        AssertBadRequest(() => arguments.Revision());
    }

    [Fact]
    public async Task DispatchRejectsNumericStringWithoutCreatingAShare()
    {
        await using Fixture fixture = new();
        Guid sessionId = fixture.Broker.CreateSession("numeric validation").Id;
        using JsonDocument arguments = JsonDocument.Parse($$"""{"sessionId":"{{sessionId}}","lifetimeMinutes":"sixty"}""");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Catalog.InvokeAsync("share.create", arguments.RootElement, fixture.Administrator, default).AsTask());

        Assert.Equal(400, ServerHttp.Classify(error).Status);
        Assert.Empty(fixture.Tokens.List(sessionId).Shares);
    }

    [Fact]
    public void NullableRevisionAndNumericDefaultsRetainTheirDocumentedBehavior()
    {
        using JsonDocument document = JsonDocument.Parse("{\"expectedRevision\":null}");
        ApiArguments arguments = new(document.RootElement);
        Assert.Null(arguments.Revision());
        Assert.Equal(60, arguments.Integer("absent", 60));
        Assert.Equal(0.5, arguments.Number("absent", 0.5));
        AssertBadRequest(() => arguments.Integer("expectedRevision", 1));
        AssertBadRequest(() => arguments.Number("expectedRevision", 0.5));
    }

    private static void AssertBadRequest(Action action)
        => Assert.Equal(400, ServerHttp.Classify(Assert.Throws<ArgumentException>(action)).Status);

    private sealed class Fixture : IAsyncDisposable
    {
        public RecordingFactory Factory { get; } = new();
        public AccessTokens Tokens { get; } = new("host-correctness-test-token-01234567890123456789");
        public ClientRegistry Clients { get; } = new();
        public MultiplexerBroker Broker { get; }
        public WorkspaceOperations Workspaces { get; }
        public BlockStreamEndpoint Streams { get; }
        public ApiCatalog Catalog { get; }
        public AccessGrant Administrator { get; }

        public Fixture()
        {
            InspectingTerminalTransportFactory inspections = new(Factory);
            Broker = new(inspections);
            Workspaces = new(Broker, inspections);
            Streams = new(Broker, Tokens, Clients);
            Catalog = new(Workspaces, Tokens, Clients);
            Administrator = Tokens.Resolve("host-correctness-test-token-01234567890123456789")!;
        }

        public ValueTask<MultiplexerBlockDescriptor> CreateBlockAsync(Guid sessionId, string transport = "pipe")
            => Workspaces.CreateBlockAsync(sessionId, new("test", transport, new("test-process", [])), default);

        public ValueTask DisposeAsync() => Broker.DisposeAsync();
    }

    private sealed class RecordingFactory : ITerminalTransportFactory
    {
        public ITerminalTransportOptions? LastOptions { get; private set; }
        public ITerminalTransport Create(ITerminalTransportOptions options)
        {
            LastOptions = options;
            return new RecordingTransport();
        }
    }

    private sealed class RecordingTransport : ITerminalTransport
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;
        public bool IsRunning { get; private set; }

        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsRunning = true;
            byte[] output = "retained-output"u8.ToArray();
            DataReceived?.Invoke(output, output.Length);
            return ValueTask.CompletedTask;
        }

        public void SendInput(ReadOnlySpan<byte> utf8) { }
        public void Resize(TerminalSessionDimensions dimensions) { }
        public ValueTask StopAsync()
        {
            if (IsRunning) { IsRunning = false; ProcessExited?.Invoke(0); }
            return ValueTask.CompletedTask;
        }
        public void Dispose() => IsRunning = false;
    }
}
