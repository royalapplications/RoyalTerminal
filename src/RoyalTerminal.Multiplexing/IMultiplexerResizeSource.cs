// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

/// <summary>Optional transport capability that orders authoritative geometry with output callbacks.</summary>
/// <remarks>A transport implementing this interface must raise the event after changing its canonical
/// parser geometry, serialized with DataReceived. It must emit rollback geometry if a resize fails.
/// It must not hold that serialization lock while calling native transport operations, which may
/// synchronously await an output callback on another thread. Consumers do not prepublish requested
/// geometry for this capability; this event is the sole source of later geometry transitions.</remarks>
public interface IMultiplexerResizeSource
{
    /// <summary>Reports applied canonical dimensions in the same order as transport output.</summary>
    event Action<TerminalSessionDimensions>? DimensionsChanged;
}
