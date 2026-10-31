// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>Connection options for one existing server-owned terminal block.</summary>
/// <param name="Endpoint">HTTP(S) base URI of the experimental multiplexer host.</param>
/// <param name="AccessToken">Bearer credential; it is never placed in a URI.</param>
/// <param name="SessionId">Owning session identifier.</param>
/// <param name="BlockId">Terminal block identifier.</param>
/// <param name="Dimensions">Desired dimensions. The client does not resize automatically during replay.</param>
public sealed record MultiplexingTransportOptions(Uri Endpoint, string AccessToken,
    Guid SessionId, Guid BlockId, TerminalSessionDimensions Dimensions) : ITerminalTransportOptions
{
    /// <summary>Provider identifier, intentionally separate from the stable built-in transport set.</summary>
    public const string Identifier = "multiplexing";

    /// <inheritdoc />
    public string TransportId => Identifier;

    /// <summary>Requests observation without input or resize privileges.</summary>
    public bool Observer { get; init; }

    /// <summary>
    /// Last applied event cursor. Set a nonzero value only when the corresponding VT state
    /// and geometry have been preserved; a fresh terminal must replay from zero.
    /// </summary>
    public long AfterSequence { get; init; }

    /// <summary>Maximum received JSON message size, including fragmented WebSocket messages.</summary>
    public int MaximumMessageBytes { get; init; } = 1024 * 1024;

    /// <summary>Maximum queued input/resize messages. Overflow fails visibly without dropping input.</summary>
    public int OutboundQueueCapacity { get; init; } = 128;

    /// <summary>Maximum size of one input operation, before base64 encoding.</summary>
    public int MaximumInputBytes { get; init; } = 64 * 1024;

    /// <summary>Maximum time to complete the authenticated attachment handshake.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Formats connection identity while excluding the bearer credential.</summary>
    public override string ToString()
        => $"MultiplexingTransportOptions {{ Endpoint = {Endpoint}, SessionId = {SessionId}, BlockId = {BlockId}, Observer = {Observer} }}";
}
