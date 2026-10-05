// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Application-owned confirmation, file inspection and denial UI for terminal hyperlinks.</summary>
public interface ITerminalHyperlinkHost
{
    /// <summary>
    /// Handles a non-direct request. Denied targets must never open; custom schemes
    /// require confirmation, and files require canonicalization plus platform safety
    /// inspection. Observe cancellation before showing UI or dispatching. Errors or
    /// cancellation never cause the control to retry through its generic launcher.
    /// </summary>
    ValueTask HandleAsync(TerminalHyperlinkRequest request, CancellationToken cancellationToken);
}
