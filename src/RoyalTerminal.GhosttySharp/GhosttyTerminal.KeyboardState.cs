// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>Gets live modifyOtherKeys mode-2 state. Serialize with terminal mutation.</summary>
    public unsafe bool GetModifyOtherKeys2()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte value = 0;
        ThrowIfFailed(GhosttyVtNative.ModifyOtherKeys2(_handle, &value), "ghostty_royal_modify_other_keys_2");
        return value != 0;
    }
}
