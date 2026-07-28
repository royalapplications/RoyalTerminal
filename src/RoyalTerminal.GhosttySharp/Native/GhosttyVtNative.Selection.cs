// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttySelectionRange
    {
        public nuint Size;
        public GhosttyGridRef Start;
        public GhosttyGridRef End;

        [MarshalAs(UnmanagedType.U1)]
        public bool Rectangle;

        public static GhosttySelectionRange CreateSized()
        {
            return new GhosttySelectionRange
            {
                Size = (nuint)Marshal.SizeOf<GhosttySelectionRange>(),
                Start = GhosttyGridRef.CreateSized(),
                End = GhosttyGridRef.CreateSized(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyTerminalSelectWordOptions
    {
        public nuint Size;
        public GhosttyGridRef Reference;
        public uint* BoundaryCodepoints;
        public nuint BoundaryCodepointsLength;

        public static GhosttyTerminalSelectWordOptions CreateSized()
        {
            return new GhosttyTerminalSelectWordOptions
            {
                Size = (nuint)Marshal.SizeOf<GhosttyTerminalSelectWordOptions>(),
                Reference = GhosttyGridRef.CreateSized(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyTerminalSelectWordBetweenOptions
    {
        public nuint Size;
        public GhosttyGridRef Start;
        public GhosttyGridRef End;
        public uint* BoundaryCodepoints;
        public nuint BoundaryCodepointsLength;

        public static GhosttyTerminalSelectWordBetweenOptions CreateSized()
        {
            return new GhosttyTerminalSelectWordBetweenOptions
            {
                Size = (nuint)Marshal.SizeOf<GhosttyTerminalSelectWordBetweenOptions>(),
                Start = GhosttyGridRef.CreateSized(),
                End = GhosttyGridRef.CreateSized(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyTerminalSelectLineOptions
    {
        public nuint Size;
        public GhosttyGridRef Reference;
        public uint* Whitespace;
        public nuint WhitespaceLength;

        [MarshalAs(UnmanagedType.U1)]
        public bool SemanticPromptBoundary;

        public static GhosttyTerminalSelectLineOptions CreateSized()
        {
            return new GhosttyTerminalSelectLineOptions
            {
                Size = (nuint)Marshal.SizeOf<GhosttyTerminalSelectLineOptions>(),
                Reference = GhosttyGridRef.CreateSized(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyTerminalSelectionFormatOptions
    {
        public nuint Size;
        public GhosttyFormatterFormat Format;

        [MarshalAs(UnmanagedType.U1)]
        public bool Unwrap;

        [MarshalAs(UnmanagedType.U1)]
        public bool Trim;

        public GhosttySelectionRange* Selection;

        public static GhosttyTerminalSelectionFormatOptions CreateSized()
        {
            return new GhosttyTerminalSelectionFormatOptions
            {
                Size = (nuint)Marshal.SizeOf<GhosttyTerminalSelectionFormatOptions>(),
            };
        }
    }

    public enum GhosttySelectionOrder : int
    {
        Forward = 0,
        Reverse = 1,
        MirroredForward = 2,
        MirroredReverse = 3,
    }

    public enum GhosttySelectionAdjust : int
    {
        Left = 0,
        Right = 1,
        Up = 2,
        Down = 3,
        Home = 4,
        End = 5,
        PageUp = 6,
        PageDown = 7,
        BeginningOfLine = 8,
        EndOfLine = 9,
    }

    public enum GhosttySelectionGestureBehavior : int
    {
        Cell = 0,
        Word = 1,
        Line = 2,
        Output = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttySelectionGestureBehaviors
    {
        public GhosttySelectionGestureBehavior SingleClick;
        public GhosttySelectionGestureBehavior DoubleClick;
        public GhosttySelectionGestureBehavior TripleClick;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttySelectionGestureGeometry
    {
        public uint Columns;
        public uint CellWidth;
        public uint PaddingLeft;
        public uint ScreenHeight;
    }

    public enum GhosttySelectionGestureAutoscroll : int
    {
        None = 0,
        Up = 1,
        Down = 2,
    }

    public enum GhosttySelectionGestureData : int
    {
        ClickCount = 0,
        Dragged = 1,
        Autoscroll = 2,
        Behavior = 3,
        Anchor = 4,
    }

    public enum GhosttySelectionGestureEventType : int
    {
        Press = 0,
        Release = 1,
        Drag = 2,
        AutoscrollTick = 3,
        DeepPress = 4,
    }

    public enum GhosttySelectionGestureEventOption : int
    {
        Reference = 0,
        Position = 1,
        RepeatDistance = 2,
        TimeNanoseconds = 3,
        RepeatIntervalNanoseconds = 4,
        WordBoundaryCodepoints = 5,
        Behaviors = 6,
        Rectangle = 7,
        Geometry = 8,
        Viewport = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttySurfacePosition
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyCodepoints
    {
        public uint* Pointer;
        public nuint Length;
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_event_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SelectionGestureEventNew(
        nint allocator,
        out nint gestureEvent,
        GhosttySelectionGestureEventType type);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_event_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SelectionGestureEventFree(nint gestureEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_event_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SelectionGestureEventSet(
        nint gestureEvent,
        GhosttySelectionGestureEventOption option,
        void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_event")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SelectionGestureEvent(
        nint gesture,
        nint terminal,
        nint gestureEvent,
        GhosttySelectionRange* selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SelectionGestureNew(nint allocator, out nint gesture);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SelectionGestureFree(nint gesture, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SelectionGestureReset(nint gesture, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SelectionGestureGet(
        nint gesture,
        nint terminal,
        GhosttySelectionGestureData data,
        void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_selection_gesture_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SelectionGestureGetMulti(
        nint gesture,
        nint terminal,
        nuint count,
        GhosttySelectionGestureData* keys,
        void** values,
        nuint* outWritten);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_select_word")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalSelectWord(
        nint terminal,
        GhosttyTerminalSelectWordOptions* options,
        ref GhosttySelectionRange selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_select_word_between")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalSelectWordBetween(
        nint terminal,
        GhosttyTerminalSelectWordBetweenOptions* options,
        ref GhosttySelectionRange selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_select_line")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalSelectLine(
        nint terminal,
        GhosttyTerminalSelectLineOptions* options,
        ref GhosttySelectionRange selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_select_all")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectAll(
        nint terminal,
        ref GhosttySelectionRange selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_select_output")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectOutput(
        nint terminal,
        GhosttyGridRef reference,
        ref GhosttySelectionRange selection);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_format_buf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalSelectionFormatBuffer(
        nint terminal,
        GhosttyTerminalSelectionFormatOptions options,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_format_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalSelectionFormatAlloc(
        nint terminal,
        nint allocator,
        GhosttyTerminalSelectionFormatOptions options,
        byte** output,
        nuint* outputLength);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_adjust")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectionAdjust(
        nint terminal,
        ref GhosttySelectionRange selection,
        GhosttySelectionAdjust adjustment);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_order")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectionOrder(
        nint terminal,
        in GhosttySelectionRange selection,
        out GhosttySelectionOrder order);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_ordered")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectionOrdered(
        nint terminal,
        in GhosttySelectionRange selection,
        GhosttySelectionOrder desired,
        ref GhosttySelectionRange output);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_contains")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectionContains(
        nint terminal,
        in GhosttySelectionRange selection,
        GhosttyPoint point,
        [MarshalAs(UnmanagedType.U1)] out bool contains);

    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_selection_equal")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult TerminalSelectionEqual(
        nint terminal,
        in GhosttySelectionRange first,
        in GhosttySelectionRange second,
        [MarshalAs(UnmanagedType.U1)] out bool equal);
}
