// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttyTerminalScrollViewportTag : int
    {
        Top = 0,
        Bottom = 1,
        Delta = 2,
        Row = 3,
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GhosttyTerminalScrollViewportValue
    {
        [FieldOffset(0)]
        public nint Delta;

        [FieldOffset(0)]
        public nuint Row;
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

        public static GhosttyTerminalScrollViewport AbsoluteRow(nuint row)
        {
            return new GhosttyTerminalScrollViewport
            {
                Tag = GhosttyTerminalScrollViewportTag.Row,
                Value = new GhosttyTerminalScrollViewportValue
                {
                    Row = row,
                },
            };
        }
    }

    public enum GhosttyTerminalCompressionMode : int
    {
        Incremental = 0,
        Full = 1,
    }

    public enum GhosttyTerminalCompressionResult : int
    {
        Unsupported = 0,
        Pending = 1,
        Complete = 2,
    }

    public enum GhosttyTerminalScreen : int
    {
        Primary = 0,
        Alternate = 1,
    }

    public enum GhosttyTerminalCursorStyle : int
    {
        Bar = 0,
        Block = 1,
        Underline = 2,
        BlockHollow = 3,
    }

    public enum GhosttyClipboardLocation : int
    {
        Standard = 0,
        Selection = 1,
        Primary = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyClipboardContent
    {
        public GhosttyString Mime;
        public GhosttyString Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyClipboardWrite
    {
        public nuint Size;
        public GhosttyClipboardLocation Location;
        public GhosttyClipboardContent* Contents;
        public nuint ContentsLength;
    }

    public enum GhosttyClipboardWriteResult : int
    {
        Success = 0,
        Denied = 1,
        Unsupported = 2,
        Busy = 3,
        InvalidData = 4,
        IoError = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalDesktopNotification
    {
        public nuint Size;
        public GhosttyString Title;
        public GhosttyString Body;
    }

    public enum GhosttyTerminalProgressState : int
    {
        Remove = 0,
        Set = 1,
        Error = 2,
        Indeterminate = 3,
        Pause = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalProgressReport
    {
        public nuint Size;
        public GhosttyTerminalProgressState State;
        public sbyte Progress;
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GhosttyTerminalPwdChangedCallback(nint terminal, nint userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate GhosttyClipboardWriteResult GhosttyTerminalClipboardWriteCallback(
        nint terminal,
        nint userdata,
        GhosttyClipboardWrite* write);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalDesktopNotificationCallback(
        nint terminal,
        nint userdata,
        GhosttyTerminalDesktopNotification* notification);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalProgressReportCallback(
        nint terminal,
        nint userdata,
        GhosttyTerminalProgressReport* report);

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
        ApcMaxBytes = 19,
        ApcMaxBytesKitty = 20,
        Selection = 21,
        DefaultCursorStyle = 22,
        DefaultCursorBlink = 23,
        GlyphProtocol = 24,
        PwdChanged = 25,
        ClipboardWrite = 26,
        ScrollbackMaxBytes = 27,
        ScrollbackMaxLines = 28,
        DesktopNotification = 29,
        ProgressReport = 30,
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
        Selection = 31,
        ViewportActive = 32,
        VtProcessingError = 33,
        ScrollbackMaxBytes = 34,
        ScrollbackMaxLines = 35,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalNew(
        nint allocator,
        out nint terminal,
        ushort columns,
        ushort rows);

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

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_compression_activity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalCompressionActivity(
        nint terminal,
        out ulong activity);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalCompress(
        nint terminal,
        GhosttyTerminalCompressionMode mode,
        out GhosttyTerminalCompressionResult result);

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

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalGetMulti(
        nint terminal,
        nuint count,
        GhosttyTerminalData* keys,
        void** values,
        nuint* outWritten);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_grid_ref")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalGridRef(
        nint terminal,
        GhosttyPoint point,
        ref GhosttyGridRef reference);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_grid_ref_track")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalGridRefTrack(
        nint terminal,
        GhosttyPoint point,
        out nint reference);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_point_from_grid_ref")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalPointFromGridRef(
        nint terminal,
        in GhosttyGridRef reference,
        GhosttyPointTag tag,
        GhosttyPointCoordinate* output);
}
