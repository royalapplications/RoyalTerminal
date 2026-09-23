// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>
    /// Advances Kitty animation playback using a monotonic millisecond clock.
    /// RoyalTerminal extension, compiled into the same library as upstream VT.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_kitty_graphics_animation_tick")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KittyGraphicsAnimationTick(
        nint graphics,
        ulong nowMilliseconds,
        out ulong nextDelayMilliseconds);
}
