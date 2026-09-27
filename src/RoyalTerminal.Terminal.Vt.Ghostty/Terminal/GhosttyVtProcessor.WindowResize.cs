// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    /// <inheritdoc />
    public Action<TerminalWindowResizeRequest>? WindowResizeCallback { get; set; }

    private void OnNativeWindowResize(nint terminal, nint userdata, ushort rows, ushort columns)
    {
        try
        {
            if (!_disposed) WindowResizeCallback?.Invoke(new(columns, rows));
        }
        catch (Exception)
        {
            // Host failures must never unwind through the native parser.
        }
    }
}
