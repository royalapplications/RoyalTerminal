// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    private GhosttyVtNative.RoyalWindowResizeCallback? _royalWindowResizeCallback;

    /// <summary>
    /// Registers and roots the CSI 8 t callback, or unregisters with null. Serialize
    /// with parser access and disposal. The callback must not throw or re-enter this
    /// terminal. No window/grid mutation occurs without the embedding host's policy.
    /// This wrapper exclusively owns the registration until removal or disposal;
    /// do not replace it through another wrapper or the raw API in the meantime.
    /// </summary>
    public void SetWindowResizeCallback(GhosttyVtNative.RoyalWindowResizeCallback? callback)
    {
        ThrowIfFailed(GhosttyVtNative.SetRoyalWindowResizeCallback(Handle, 0,
            callback is null ? 0 : Marshal.GetFunctionPointerForDelegate(callback)), "ghostty_royal_window_resize_callback");
        _royalWindowResizeCallback = callback;
    }
}
