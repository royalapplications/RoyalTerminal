// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Win32.SafeHandles;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Keeps ConPTY alive during native calls and releases it independently of its reader.
/// </summary>
internal sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly Action<nint> _close;

    internal SafePseudoConsoleHandle(nint value, Action<nint> close)
        : base(ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(close);
        _close = close;
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        nint value = handle;
        Action<nint> close = _close;
        // HasExited may raise Process.Exited on its caller. The final native
        // lease may also return on the reader or UI thread. Never close inline:
        // before Windows 11 24H2, close can wait for the output reader to drain.
        // https://learn.microsoft.com/windows/console/closepseudoconsole#remarks
        Thread worker = new(() => close(value))
        {
            IsBackground = true,
            Name = "WindowsPty-Close",
        };
        worker.Start();
        return true;
    }
}
