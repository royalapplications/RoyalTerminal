// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Effective encoder state, independent of the saved/current mode bank.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RoyalMouseState
    {
        /// <summary>Structure size in bytes.</summary>
        public nuint Size;
        /// <summary>Effective GhosttyMouseTrackingMode value.</summary>
        public uint Tracking;
        /// <summary>Effective GhosttyMouseFormat value.</summary>
        public uint Format;
        /// <summary>Initializes the required size field.</summary>
        public static RoyalMouseState CreateSized() => new() { Size = (nuint)Unsafe.SizeOf<RoyalMouseState>() };
    }

    /// <summary>Copies effective mouse state. Serialize with terminal mutation.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_mouse_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult MouseState(nint terminal, RoyalMouseState* output);
}
