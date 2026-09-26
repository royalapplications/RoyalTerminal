// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Copies modifyOtherKeys mode-2 state as 0/1. Serialize with terminal mutation.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_modify_other_keys_2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult ModifyOtherKeys2(nint terminal, byte* output);
}
