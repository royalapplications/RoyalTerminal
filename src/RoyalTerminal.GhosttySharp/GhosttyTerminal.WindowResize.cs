// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    private GhosttyVtNative.GhosttyTerminalResizeWindowCallback? _windowResizeCallback;

    /// <summary>
    /// Registers and roots the CSI 8 t callback, or unregisters with null. Serialize
    /// with parser access and disposal. The callback must not throw or re-enter this
    /// terminal. No window/grid mutation occurs without the embedding host's policy.
    /// This wrapper exclusively owns the registration until removal or disposal;
    /// do not replace it through another wrapper or the raw API in the meantime.
    /// </summary>
    public unsafe void SetWindowResizeCallback(GhosttyVtNative.RoyalWindowResizeCallback? callback)
    {
        GhosttyVtNative.GhosttyTerminalResizeWindowCallback? bridge = callback is null ? null :
            (terminal, _, request) =>
            {
                if (request is not null && request->Tag == GhosttyVtNative.GhosttyTerminalResizeWindowTag.Grid)
                    callback(terminal, 0, request->Value.Grid.Rows, request->Value.Grid.Columns);
            };
        SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.ResizeWindow,
            bridge is null ? 0 : Marshal.GetFunctionPointerForDelegate(bridge));
        _windowResizeCallback = bridge;
    }

    private unsafe void ClearWindowResizeCallback()
    {
        if (_windowResizeCallback is null || _handle == nint.Zero) return;
        GhosttyVtNative.TerminalSet(_handle, GhosttyVtNative.GhosttyTerminalOption.ResizeWindow, null);
        GC.KeepAlive(_windowResizeCallback);
        _windowResizeCallback = null;
    }
}
