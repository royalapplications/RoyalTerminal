// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private Action<TerminalWindowResizeRequest>? _windowResizeCallback;

    /// <inheritdoc />
    public Action<TerminalWindowResizeRequest>? WindowResizeCallback
    {
        get => _windowResizeCallback;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_windowResizeCallback, value)) return;
            // Disabled hosts incur no unmanaged-to-managed callback transition.
            _terminal.SetWindowResizeCallback(value is null ? null : OnNativeWindowResize);
            _windowResizeCallback = value;
        }
    }

    private void OnNativeWindowResize(nint terminal, nint userdata, ushort rows, ushort columns)
    {
        try
        {
            if (!_disposed) _windowResizeCallback?.Invoke(new(columns, rows));
        }
        catch (Exception)
        {
            // Host failures must never unwind through the native parser.
        }
    }
}
