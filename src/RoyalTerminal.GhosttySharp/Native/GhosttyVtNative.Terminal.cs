// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalOptions
    {
        public ushort Cols;
        public ushort Rows;
        public nuint MaxScrollback;
    }

    public enum GhosttyTerminalScrollViewportTag : int
    {
        Top = 0,
        Bottom = 1,
        Delta = 2,
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GhosttyTerminalScrollViewportValue
    {
        [FieldOffset(0)]
        public nint Delta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalScrollViewport
    {
        public GhosttyTerminalScrollViewportTag Tag;
        public GhosttyTerminalScrollViewportValue Value;

        public static GhosttyTerminalScrollViewport Top()
        {
            return new GhosttyTerminalScrollViewport
            {
                Tag = GhosttyTerminalScrollViewportTag.Top,
            };
        }

        public static GhosttyTerminalScrollViewport Bottom()
        {
            return new GhosttyTerminalScrollViewport
            {
                Tag = GhosttyTerminalScrollViewportTag.Bottom,
            };
        }

        public static GhosttyTerminalScrollViewport DeltaRows(int delta)
        {
            return new GhosttyTerminalScrollViewport
            {
                Tag = GhosttyTerminalScrollViewportTag.Delta,
                Value = new GhosttyTerminalScrollViewportValue
                {
                    Delta = delta,
                },
            };
        }
    }

    public enum GhosttyTerminalScreen : int
    {
        Primary = 0,
        Alternate = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalScrollbar
    {
        public ulong Total;
        public ulong Offset;
        public ulong Length;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GhosttyTerminalBellCallback(nint terminal, nint userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GhosttyTerminalWritePtyCallback(nint terminal, nint userdata, nint data, nuint len);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GhosttyTerminalTitleChangedCallback(nint terminal, nint userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate GhosttyString GhosttyTerminalEnquiryCallback(nint terminal, nint userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate GhosttyString GhosttyTerminalXtversionCallback(nint terminal, nint userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate byte GhosttyTerminalSizeCallback(nint terminal, nint userdata, GhosttySizeReportSize* size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate byte GhosttyTerminalColorSchemeCallback(nint terminal, nint userdata, GhosttyColorScheme* scheme);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate byte GhosttyTerminalDeviceAttributesCallback(nint terminal, nint userdata, GhosttyDeviceAttributes* attributes);

    public enum GhosttyTerminalOption : int
    {
        Userdata = 0,
        WritePty = 1,
        Bell = 2,
        Enquiry = 3,
        Xtversion = 4,
        TitleChanged = 5,
        Size = 6,
        ColorScheme = 7,
        DeviceAttributes = 8,
        Title = 9,
        Pwd = 10,
        ColorForeground = 11,
        ColorBackground = 12,
        ColorCursor = 13,
        ColorPalette = 14,
        KittyImageStorageLimit = 15,
        KittyImageMediumFile = 16,
        KittyImageMediumTempFile = 17,
        KittyImageMediumSharedMemory = 18,
    }

    public enum GhosttyTerminalData : int
    {
        Invalid = 0,
        Cols = 1,
        Rows = 2,
        CursorX = 3,
        CursorY = 4,
        CursorPendingWrap = 5,
        ActiveScreen = 6,
        CursorVisible = 7,
        KittyKeyboardFlags = 8,
        Scrollbar = 9,
        CursorStyle = 10,
        MouseTracking = 11,
        Title = 12,
        Pwd = 13,
        TotalRows = 14,
        ScrollbackRows = 15,
        WidthPx = 16,
        HeightPx = 17,
        ColorForeground = 18,
        ColorBackground = 19,
        ColorCursor = 20,
        ColorPalette = 21,
        ColorForegroundDefault = 22,
        ColorBackgroundDefault = 23,
        ColorCursorDefault = 24,
        ColorPaletteDefault = 25,
        KittyImageStorageLimit = 26,
        KittyImageMediumFile = 27,
        KittyImageMediumTempFile = 28,
        KittyImageMediumSharedMemory = 29,
        KittyGraphics = 30,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalNew(
        nint allocator,
        out nint terminal,
        GhosttyTerminalOptions options);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TerminalFree(nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TerminalReset(nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_resize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalResize(
        nint terminal,
        ushort cols,
        ushort rows,
        uint cellWidthPx,
        uint cellHeightPx);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalSet(
        nint terminal,
        GhosttyTerminalOption option,
        void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_vt_write")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void TerminalVtWrite(nint terminal, byte* data, nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_scroll_viewport")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void TerminalScrollViewport(nint terminal, GhosttyTerminalScrollViewport behavior);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_mode_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalModeGet(
        nint terminal,
        GhosttyMode mode,
        [MarshalAs(UnmanagedType.U1)] out bool value);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_mode_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalModeSet(
        nint terminal,
        GhosttyMode mode,
        [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalGet(
        nint terminal,
        GhosttyTerminalData data,
        void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_grid_ref")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalGridRef(
        nint terminal,
        GhosttyPoint point,
        ref GhosttyGridRef reference);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_point_from_grid_ref")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalPointFromGridRef(
        nint terminal,
        in GhosttyGridRef reference,
        GhosttyPointTag tag,
        GhosttyPointCoordinate* output);
}
