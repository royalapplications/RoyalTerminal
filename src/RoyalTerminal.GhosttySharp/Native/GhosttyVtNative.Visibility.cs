// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Copies host visibility as 0/1; invalid arguments leave output untouched.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_visibility_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult VisibilityGet(nint terminal, byte* output);

    /// <summary>
    /// Updates host visibility as 0/1 and copies an enabled transition report.
    /// Nine output bytes suffice. OutOfSpace sets written to required capacity
    /// without mutation; invalid arguments leave state and outputs unchanged.
    /// Serialize with terminal access; send returned bytes in response order.
    /// </summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_visibility_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult VisibilitySet(nint terminal, byte value,
        byte* output, nuint capacity, nuint* written);
}
