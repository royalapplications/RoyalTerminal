// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>
    /// Gets or sets live host password-input metadata. Does not change OS secure input.
    /// Serialize with all terminal access. Full reset clears the flag.
    /// </summary>
    public unsafe bool PasswordInput
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            byte value = 0;
            ThrowIfFailed(GhosttyVtNative.PasswordInputGet(_handle, &value), "ghostty_royal_password_input_get");
            return value != 0;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfFailed(GhosttyVtNative.PasswordInputSet(_handle, value ? (byte)1 : (byte)0), "ghostty_royal_password_input_set");
        }
    }
}
