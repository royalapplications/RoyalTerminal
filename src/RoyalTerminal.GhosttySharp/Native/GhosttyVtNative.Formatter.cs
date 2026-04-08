// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttyFormatterFormat : int
    {
        Plain = 0,
        Vt = 1,
        Html = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyFormatterScreenExtra
    {
        public nuint Size;

        [MarshalAs(UnmanagedType.U1)]
        public bool Cursor;

        [MarshalAs(UnmanagedType.U1)]
        public bool Style;

        [MarshalAs(UnmanagedType.U1)]
        public bool Hyperlink;

        [MarshalAs(UnmanagedType.U1)]
        public bool Protection;

        [MarshalAs(UnmanagedType.U1)]
        public bool KittyKeyboard;

        [MarshalAs(UnmanagedType.U1)]
        public bool Charsets;

        public static GhosttyFormatterScreenExtra CreateSized()
        {
            return new GhosttyFormatterScreenExtra
            {
                Size = (nuint)Marshal.SizeOf<GhosttyFormatterScreenExtra>(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyFormatterTerminalExtra
    {
        public nuint Size;

        [MarshalAs(UnmanagedType.U1)]
        public bool Palette;

        [MarshalAs(UnmanagedType.U1)]
        public bool Modes;

        [MarshalAs(UnmanagedType.U1)]
        public bool ScrollingRegion;

        [MarshalAs(UnmanagedType.U1)]
        public bool Tabstops;

        [MarshalAs(UnmanagedType.U1)]
        public bool Pwd;

        [MarshalAs(UnmanagedType.U1)]
        public bool Keyboard;

        public GhosttyFormatterScreenExtra Screen;

        public static GhosttyFormatterTerminalExtra CreateSized()
        {
            return new GhosttyFormatterTerminalExtra
            {
                Size = (nuint)Marshal.SizeOf<GhosttyFormatterTerminalExtra>(),
                Screen = GhosttyFormatterScreenExtra.CreateSized(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyFormatterTerminalOptions
    {
        public nuint Size;
        public GhosttyFormatterFormat Emit;

        [MarshalAs(UnmanagedType.U1)]
        public bool Unwrap;

        [MarshalAs(UnmanagedType.U1)]
        public bool Trim;

        public GhosttyFormatterTerminalExtra Extra;

        public GhosttySelectionRange* Selection;

        public static GhosttyFormatterTerminalOptions CreateSized()
        {
            return new GhosttyFormatterTerminalOptions
            {
                Size = (nuint)Marshal.SizeOf<GhosttyFormatterTerminalOptions>(),
                Extra = GhosttyFormatterTerminalExtra.CreateSized(),
            };
        }
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_formatter_terminal_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult FormatterTerminalNew(
        nint allocator,
        out nint formatter,
        nint terminal,
        GhosttyFormatterTerminalOptions options);

    [LibraryImport(LibName, EntryPoint = "ghostty_formatter_format_buf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult FormatterFormatBuffer(
        nint formatter,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_formatter_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void FormatterFree(nint formatter);
}
