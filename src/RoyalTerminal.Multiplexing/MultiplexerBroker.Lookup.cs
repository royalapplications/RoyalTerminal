// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;

public sealed partial class MultiplexerBroker
{
    /// <summary>Capture one terminal's current state and placement, scoped to its session, without materializing the rest of the workspace.</summary>
    public MultiplexerBlockDescriptor GetBlock(Guid sessionId, Guid blockId)
    {
        lock (_gate)
        {
            var session = FindSession(sessionId);
            return DescribeBlock(session, FindBlock(session, blockId));
        }
    }
}
