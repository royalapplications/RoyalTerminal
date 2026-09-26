// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional terminal capability for changes that become visible without additional input,
/// such as image animation or an expired synchronized-output hold.
/// </summary>
/// <remarks>Access this capability under the same state lock used for processing terminal output.</remarks>
public interface ITerminalTimedRefreshSource
{
    /// <summary>
    /// Gets the remaining delay until the next refresh, relative to the time of this call,
    /// or null when no timed refresh is needed. Zero requests an immediate refresh.
    /// </summary>
    TimeSpan? NextTimedRefreshDelay { get; }

    /// <summary>Advances time-dependent terminal state using the processor's monotonic clock.</summary>
    /// <returns>True when terminal screen contents changed and should be presented.</returns>
    bool RefreshTimedState();
}
