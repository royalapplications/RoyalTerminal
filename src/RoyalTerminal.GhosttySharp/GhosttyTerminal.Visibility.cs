// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>
    /// Gets host visibility, independent of focus. Defaults to potentially visible;
    /// survives resets and is not snapshot content. Serialize with terminal access.
    /// </summary>
    public unsafe bool PotentiallyVisible
    {
        get
        {
            byte value = 0;
            ThrowIfFailed(GhosttyVtNative.VisibilityGet(Handle, &value), "ghostty_royal_visibility_get");
            return value != 0;
        }
    }

    /// <summary>
    /// Sets host visibility without entering the VT parser. Returns an owned report
    /// only for a change while mode 2033 is enabled; otherwise returns an empty array.
    /// Send returned bytes to the client in terminal-response order. This does not
    /// invoke the native write callback. Serialize with all terminal access.
    /// </summary>
    public unsafe byte[] SetVisibility(bool potentiallyVisible)
    {
        Span<byte> response = stackalloc byte[9];
        nuint written = 0;
        fixed (byte* output = response)
            ThrowIfFailed(GhosttyVtNative.VisibilitySet(Handle, potentiallyVisible ? (byte)1 : (byte)0,
                output, (nuint)response.Length, &written), "ghostty_royal_visibility_set");
        return response[..checked((int)written)].ToArray();
    }
}
