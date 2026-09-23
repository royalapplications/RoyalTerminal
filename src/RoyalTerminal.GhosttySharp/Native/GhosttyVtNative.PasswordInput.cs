// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Copies host password-input metadata as 0/1; invalid arguments leave output untouched.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_password_input_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult PasswordInputGet(nint terminal, byte* output);

    /// <summary>Sets host password-input metadata as 0/1; invalid arguments leave state unchanged.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_royal_password_input_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult PasswordInputSet(nint terminal, byte value);
}
