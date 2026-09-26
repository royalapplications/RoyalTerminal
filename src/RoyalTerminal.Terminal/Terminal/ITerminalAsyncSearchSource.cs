// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Progress of the current search over a published terminal snapshot.</summary>
public enum TerminalSearchStatus
{
    /// <summary>Current matches are provisional while history is searched.</summary>
    Pending,
    /// <summary>All matches belong to the current published snapshot.</summary>
    Complete,
    /// <summary>The background scan failed; inspect the source's error.</summary>
    Failed,
}

/// <summary>
/// Optional nonblocking search capability. Calls require the terminal's screen
/// lock. Results are absolute coordinates in the current published buffer;
/// stale generations must never be returned. Completion wakes the host through
/// <see cref="ITerminalTimedRefreshSource"/>.
/// </summary>
public interface ITerminalAsyncSearchSource
{
    /// <summary>Requests or refreshes a search and copies its currently available matches.</summary>
    /// <param name="needle">Literal search text; empty text cancels the search.</param>
    /// <param name="destination">Caller-owned list, cleared before copying current matches.</param>
    /// <returns>Whether history scanning is pending, complete, or failed.</returns>
    TerminalSearchStatus PopulateSearchMatchesAsync(string needle, List<TerminalSearchMatch> destination);

    /// <summary>The failure for the current request, or null when the request has not failed.</summary>
    Exception? SearchError { get; }

    /// <summary>Cancels pending work and releases cached search snapshots and results.</summary>
    void CancelSearch();
}
