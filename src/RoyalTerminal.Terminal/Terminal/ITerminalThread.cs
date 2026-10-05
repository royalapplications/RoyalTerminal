// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>An owned, joinable terminal worker. Cancellation belongs to its caller.</summary>
public interface ITerminalThread
{
    /// <summary>Whether the caller is executing on this worker.</summary>
    bool IsCurrent { get; }

    /// <summary>Starts the worker exactly once, flowing the caller's execution context. Throws if already started.</summary>
    void Start();

    /// <summary>
    /// Waits for thread exit and releases its native resources. Concurrent/repeated joins
    /// are supported. Rethrows a callback failure; joining before Start or from the worker throws.
    /// </summary>
    void Join();
}
