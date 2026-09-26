// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    private GhosttyVtNative.RoyalNotificationCallback? _royalNotificationCallback;

    /// <summary>
    /// Sets and roots the OSC 99 callback, or unregisters with null. Serialize with
    /// parser access and disposal. The callback must not throw or retain borrowed pointers.
    /// </summary>
    public void SetNotificationCallback(GhosttyVtNative.RoyalNotificationCallback? callback)
    {
        ThrowIfFailed(GhosttyVtNative.SetRoyalNotificationCallback(Handle, 0,
            callback is null ? 0 : Marshal.GetFunctionPointerForDelegate(callback)), "ghostty_royal_notification_callback");
        _royalNotificationCallback = callback;
    }
}
