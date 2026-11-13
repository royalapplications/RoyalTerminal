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
        /// <summary>Move by semantic prompt boundaries.</summary>
        DeltaPrompt = 4,
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GhosttyTerminalScrollViewportValue
    {
        [FieldOffset(0)]
        public nint Delta;

        [FieldOffset(0)]
        public nuint Row;

        /// <summary>Signed prompt count; positive moves toward newer prompts.</summary>
        [FieldOffset(0)]
        public nint DeltaPrompt;
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

        /// <summary>Moves by prompt boundaries, clamping to the terminal ends.</summary>
        public static GhosttyTerminalScrollViewport DeltaPrompts(nint delta) => new()
        {
            Tag = GhosttyTerminalScrollViewportTag.DeltaPrompt,
            Value = new GhosttyTerminalScrollViewportValue { DeltaPrompt = delta },
        };
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
        public GhosttyClipboardLocation Location;
        public GhosttyClipboardContent* Contents;
        public nuint ContentsLength;
        public GhosttyString Name;

        [MarshalAs(UnmanagedType.U1)]
        public bool Granted;

        [MarshalAs(UnmanagedType.U1)]
        public bool CanRemember;

        public nint Context;
        public nint Reply;
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
    public struct GhosttyClipboardWriteReply
    {
        public nuint Size;
        public GhosttyClipboardWriteResult Result;

        [MarshalAs(UnmanagedType.U1)]
        public bool Remember;

        public static GhosttyClipboardWriteReply CreateSized(GhosttyClipboardWriteResult result)
        {
            return new GhosttyClipboardWriteReply
            {
                Size = (nuint)Marshal.SizeOf<GhosttyClipboardWriteReply>(),
                Result = result,
            };
        }
    }

    public enum GhosttyClipboardReadResult : int
    {
        Success = 0,
        Denied = 1,
        Unsupported = 2,
        Busy = 3,
        IoError = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyClipboardReadReply
    {
        public nuint Size;
        public GhosttyClipboardReadResult Result;
        public GhosttyClipboardContent* Contents;
        public nuint ContentsLength;
        public GhosttyString* Available;
        public nuint AvailableLength;

        [MarshalAs(UnmanagedType.U1)]
        public bool Remember;

        public static GhosttyClipboardReadReply CreateSized(GhosttyClipboardReadResult result)
        {
            return new GhosttyClipboardReadReply
            {
                Size = (nuint)Marshal.SizeOf<GhosttyClipboardReadReply>(),
                Result = result,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyClipboardRead
    {
        public GhosttyClipboardLocation Location;
        public GhosttyString* Mimes;
        public nuint MimesLength;

        [MarshalAs(UnmanagedType.U1)]
        public bool List;

        public GhosttyString Name;

        [MarshalAs(UnmanagedType.U1)]
        public bool Granted;

        [MarshalAs(UnmanagedType.U1)]
        public bool CanRemember;

        public nint Context;
        public nint Reply;
    }

    public enum GhosttyTerminalUnknownSequenceTag : int
    {
        Apc = 0,
        /// <summary>Unsupported OSC sequence.</summary>
        Osc = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalUnknownStringSequence
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool Truncated;

        public GhosttyString Content;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct GhosttyTerminalUnknownSequenceValue
    {
        [FieldOffset(0)]
        public GhosttyTerminalUnknownStringSequence Apc;

        /// <summary>Unsupported OSC content and terminator.</summary>
        [FieldOffset(0)]
        public GhosttyTerminalUnknownOscSequence Osc;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalUnknownSequence
    {
        public GhosttyTerminalUnknownSequenceTag Tag;
        public GhosttyTerminalUnknownSequenceValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalModeConfig
    {
        public GhosttyMode Mode;

        [MarshalAs(UnmanagedType.U1)]
        public bool Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalDesktopNotification
    {
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
    public unsafe delegate void GhosttyTerminalClipboardWriteCallback(
        nint terminal,
        nint userdata,
        GhosttyClipboardWrite* write);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyClipboardWriteReplyCallback(
        GhosttyClipboardWrite* write,
        GhosttyClipboardWriteReply* reply);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalClipboardReadCallback(
        nint terminal,
        nint userdata,
        GhosttyClipboardRead* read);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyClipboardReadReplyCallback(
        GhosttyClipboardRead* read,
        GhosttyClipboardReadReply* reply);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalUnknownSequenceCallback(
        nint terminal,
        nint userdata,
        GhosttyTerminalUnknownSequence* sequence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GhosttyTerminalRenderHoldCallback(
        nint terminal,
        nint userdata,
        [MarshalAs(UnmanagedType.U1)] bool held);

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
        Userdata = 1,
        WritePty = 2,
        Bell = 3,
        Enquiry = 4,
        Xtversion = 5,
        TitleChanged = 6,
        Size = 7,
        ColorScheme = 8,
        DeviceAttributes = 9,
        Title = 10,
        Pwd = 11,
        ColorForeground = 12,
        ColorBackground = 13,
        ColorCursor = 14,
        ColorPalette = 15,
        KittyImageStorageLimit = 16,
        KittyImageMediumFile = 17,
        KittyImageMediumTempFile = 18,
        KittyImageMediumSharedMemory = 19,
        ApcMaxBytes = 20,
        ApcMaxBytesKitty = 21,
        Selection = 22,
        DefaultCursorStyle = 23,
        DefaultCursorBlink = 24,
        GlyphProtocol = 25,
        PwdChanged = 26,
        ClipboardWrite = 27,
        ScrollbackMaxBytes = 28,
        ScrollbackMaxLines = 29,
        DesktopNotification = 30,
        ProgressReport = 31,
        ContinuationMaxBytes = 32,
        TitleReport = 33,
        ModeDefault = 34,
        Mode = 35,
        UnknownSequence = 36,
        UnknownMaxBytes = 37,
        TerminfoName = 38,
        ClipboardRead = 39,
        ClipboardWriteMaxBytes = 40,
        ResizePullScrollback = 41,
        RenderHold = 42,
        /// <summary>OSC 133 lifecycle callback.</summary>
        SemanticPrompt = 43,
        /// <summary>RIS callback.</summary>
        Reset = 44,
        /// <summary>Enable DECRQCRA replies.</summary>
        XtChecksumReport = 45,
        /// <summary>Default XTCHECKSUM flags (0–31).</summary>
        XtChecksumExtension = 46,
        /// <summary>OSC 7501 report callback; enables feature detection.</summary>
        ProgramStatus = 47,
        /// <summary>Host callback for terminal-requested window resizing.</summary>
        ResizeWindow = 48,
        /// <summary>Upstream INVALID value.</summary>
        Invalid = 0,
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
        ContinuationMaxBytes = 36,
        Mode = 37,
        VtGround = 38,
        CursorAtPrompt = 39,
        ClipboardWriteMaxBytes = 40,
        /// <summary>Application-requested pointer shape.</summary>
        MouseShape = 41,
        /// <summary>Native page and image memory usage.</summary>
        MemoryUsage = 42,
        /// <summary>Upstream CURSOR_SGR_STYLE value.</summary>
        CursorSgrStyle = 10,
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

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_continuation_write")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalContinuationWrite(nint terminal, GhosttyWriter writer);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_continuation_buf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalContinuationBuffer(
        nint terminal,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_continuation_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalContinuationAlloc(
        nint terminal,
        GhosttyAllocator* allocator,
        byte** output,
        out nuint outputLength);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_scroll_viewport")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalScrollViewport(nint terminal, GhosttyTerminalScrollViewport behavior);

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
