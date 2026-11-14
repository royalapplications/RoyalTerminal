// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

public sealed partial class MultiplexerBroker
{
    /// <summary>Appends a block under the same atomic revision check as splitting a leaf. Null window selects the first window.</summary>
    public ValueTask<MultiplexerBlockDescriptor> AppendBlockAsync(Guid sessionId, Guid? windowId, string label,
        ITerminalTransportOptions options, long? expectedRevision, CancellationToken cancellationToken = default) =>
        StartBlockAsync(sessionId, windowId, null, MultiplexerSplitOrientation.Horizontal, .5, label, options, expectedRevision, cancellationToken);
}
