// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>Opt-in factory participant for server-owned terminal blocks.</summary>
public sealed class MultiplexingTerminalTransportProvider : ITerminalTransportProvider
{
    private readonly IMultiplexingSocketConnector _connector;

    /// <summary>Creates a provider using the system WebSocket implementation.</summary>
    public MultiplexingTerminalTransportProvider() : this(new MultiplexingSocketConnector()) { }

    /// <summary>Creates a provider using an explicit connection boundary.</summary>
    public MultiplexingTerminalTransportProvider(IMultiplexingSocketConnector connector)
        => _connector = connector ?? throw new ArgumentNullException(nameof(connector));

    /// <inheritdoc />
    public string TransportId => MultiplexingTransportOptions.Identifier;

    /// <inheritdoc />
    public bool CanHandle(ITerminalTransportOptions options) => options is MultiplexingTransportOptions;

    /// <inheritdoc />
    public ITerminalTransport Create() => new MultiplexingTerminalTransport(_connector);
}
