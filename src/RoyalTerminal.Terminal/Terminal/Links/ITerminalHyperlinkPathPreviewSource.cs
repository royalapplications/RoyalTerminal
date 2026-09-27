// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Optional host resolution of file-URL and scheme-less hyperlink previews.</summary>
public interface ITerminalHyperlinkPathPreviewSource
{
    /// <summary>
    /// Returns a display-only effective path. Keep filesystem work off the caller's
    /// thread and observe cancellation. The control escapes unsafe scalars again;
    /// neither this result nor a cached preview ever grants permission to launch.
    /// </summary>
    ValueTask<string> GetPathPreviewAsync(string target, CancellationToken cancellationToken);
}
