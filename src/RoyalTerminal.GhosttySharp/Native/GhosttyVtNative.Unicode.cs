// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [LibraryImport(LibName, EntryPoint = "ghostty_unicode_codepoint_width")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte UnicodeCodepointWidth(uint codepoint);

    [LibraryImport(LibName, EntryPoint = "ghostty_unicode_grapheme_width")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial nuint UnicodeGraphemeWidth(
        uint* codepoints,
        nuint length,
        byte* width);
}
