// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RoyalHistoryInfo
    {
        internal nuint Size;
        internal ulong Epoch;
        internal ulong Origin;
        internal ulong ScreenGeneration;
        internal ulong TotalRows;
        internal ushort Columns;
        internal ushort Rows;
        internal byte Alternate;
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_royal_history_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial GhosttyResult RoyalHistoryGet(nint terminal, ref RoyalHistoryInfo info);

    [LibraryImport(LibName, EntryPoint = "ghostty_royal_history_row_ref")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial GhosttyResult RoyalHistoryRowRef(nint terminal, ulong row, ref GhosttyGridRef reference);

    [LibraryImport(LibName, EntryPoint = "ghostty_royal_history_grapheme_fits")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial GhosttyResult RoyalHistoryGraphemeFits(in GhosttyGridRef reference, nuint budget);
}
