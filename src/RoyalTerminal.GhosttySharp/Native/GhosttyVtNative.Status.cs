// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Application-requested OSC 22 mouse shape.</summary>
    public enum GhosttyMouseShape : int
    {
        /// <summary>default.</summary>
        Default = 0,
        /// <summary>context menu.</summary>
        ContextMenu = 1,
        /// <summary>help.</summary>
        Help = 2,
        /// <summary>pointer.</summary>
        Pointer = 3,
        /// <summary>progress.</summary>
        Progress = 4,
        /// <summary>wait.</summary>
        Wait = 5,
        /// <summary>cell.</summary>
        Cell = 6,
        /// <summary>crosshair.</summary>
        Crosshair = 7,
        /// <summary>text.</summary>
        Text = 8,
        /// <summary>vertical text.</summary>
        VerticalText = 9,
        /// <summary>alias.</summary>
        Alias = 10,
        /// <summary>copy.</summary>
        Copy = 11,
        /// <summary>move.</summary>
        Move = 12,
        /// <summary>no drop.</summary>
        NoDrop = 13,
        /// <summary>not allowed.</summary>
        NotAllowed = 14,
        /// <summary>grab.</summary>
        Grab = 15,
        /// <summary>grabbing.</summary>
        Grabbing = 16,
        /// <summary>all scroll.</summary>
        AllScroll = 17,
        /// <summary>col resize.</summary>
        ColResize = 18,
        /// <summary>row resize.</summary>
        RowResize = 19,
        /// <summary>n resize.</summary>
        NResize = 20,
        /// <summary>e resize.</summary>
        EResize = 21,
        /// <summary>s resize.</summary>
        SResize = 22,
        /// <summary>w resize.</summary>
        WResize = 23,
        /// <summary>ne resize.</summary>
        NeResize = 24,
        /// <summary>nw resize.</summary>
        NwResize = 25,
        /// <summary>se resize.</summary>
        SeResize = 26,
        /// <summary>sw resize.</summary>
        SwResize = 27,
        /// <summary>ew resize.</summary>
        EwResize = 28,
        /// <summary>ns resize.</summary>
        NsResize = 29,
        /// <summary>nesw resize.</summary>
        NeswResize = 30,
        /// <summary>nwse resize.</summary>
        NwseResize = 31,
        /// <summary>zoom in.</summary>
        ZoomIn = 32,
        /// <summary>zoom out.</summary>
        ZoomOut = 33,
    }

    /// <summary>Terminator to preserve when answering an OSC query.</summary>
    public enum GhosttyOscTerminator : int
    {
        /// <summary>st.</summary>
        St = 0,
        /// <summary>bel.</summary>
        Bel = 1,
    }

    /// <summary>Options for the standalone OSC parser.</summary>
    public enum GhosttyOscOption : int
    {
        /// <summary>unknown max bytes.</summary>
        UnknownMaxBytes = 0,
    }

    /// <summary>Native page and image memory usage; excludes host allocations.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalMemoryUsage
    {
        /// <summary>Size.</summary>
        public nuint Size;
        /// <summary>Compression supported.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool CompressionSupported;
        /// <summary>Primary pages.</summary>
        public ulong PrimaryPages;
        /// <summary>Primary virtual bytes.</summary>
        public ulong PrimaryVirtualBytes;
        /// <summary>Primary resident bytes.</summary>
        public ulong PrimaryResidentBytes;
        /// <summary>Primary compressed pages.</summary>
        public ulong PrimaryCompressedPages;
        /// <summary>Primary compressed bytes.</summary>
        public ulong PrimaryCompressedBytes;
        /// <summary>Primary image bytes.</summary>
        public ulong PrimaryImageBytes;
        /// <summary>Alternate pages.</summary>
        public ulong AlternatePages;
        /// <summary>Alternate virtual bytes.</summary>
        public ulong AlternateVirtualBytes;
        /// <summary>Alternate resident bytes.</summary>
        public ulong AlternateResidentBytes;
        /// <summary>Alternate compressed pages.</summary>
        public ulong AlternateCompressedPages;
        /// <summary>Alternate compressed bytes.</summary>
        public ulong AlternateCompressedBytes;
        /// <summary>Alternate image bytes.</summary>
        public ulong AlternateImageBytes;
    }

    /// <summary>Borrowed unsupported OSC, including its command number and terminator.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalUnknownOscSequence
    {
        /// <summary>Truncated.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool Truncated;
        /// <summary>Content.</summary>
        public GhosttyString Content;
        /// <summary>Terminator.</summary>
        public GhosttyOscTerminator Terminator;
    }

    /// <summary>Validated OSC 7501 program state.</summary>
    public enum GhosttyProgramStatusState : int
    {
        /// <summary>idle.</summary>
        Idle = 0,
        /// <summary>working.</summary>
        Working = 1,
        /// <summary>done.</summary>
        Done = 2,
        /// <summary>blocked.</summary>
        Blocked = 3,
        /// <summary>error.</summary>
        Error = 4,
        /// <summary>clear.</summary>
        Clear = 5,
    }

    /// <summary>Reason that a program is blocked.</summary>
    public enum GhosttyProgramStatusKind : int
    {
        /// <summary>none.</summary>
        None = 0,
        /// <summary>permission.</summary>
        Permission = 1,
        /// <summary>question.</summary>
        Question = 2,
        /// <summary>auth.</summary>
        Auth = 3,
    }

    /// <summary>Borrowed OSC 7501 report, valid only for the callback duration.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalProgramStatus
    {
        /// <summary>Size.</summary>
        public nuint Size;
        /// <summary>State.</summary>
        public GhosttyProgramStatusState State;
        /// <summary>Kind.</summary>
        public GhosttyProgramStatusKind Kind;
        /// <summary>Progress.</summary>
        public sbyte Progress;
        /// <summary>Id.</summary>
        public GhosttyString Id;
        /// <summary>App.</summary>
        public GhosttyString App;
        /// <summary>Title.</summary>
        public GhosttyString Title;
        /// <summary>Message.</summary>
        public GhosttyString Message;
    }

    /// <summary>OSC 133 lifecycle event kind.</summary>
    public enum GhosttySemanticPromptKind : int
    {
        /// <summary>invalid.</summary>
        Invalid = 0,
        /// <summary>start.</summary>
        PromptStart = 1,
        /// <summary>input start.</summary>
        InputStart = 2,
        /// <summary>output start.</summary>
        OutputStart = 3,
        /// <summary>command end.</summary>
        CommandEnd = 4,
    }

    /// <summary>Kind of shell prompt.</summary>
    public enum GhosttySemanticPromptPromptKind : int
    {
        /// <summary>primary.</summary>
        Primary = 0,
        /// <summary>right.</summary>
        Right = 1,
        /// <summary>continuation.</summary>
        Continuation = 2,
        /// <summary>secondary.</summary>
        Secondary = 3,
    }

    /// <summary>Borrowed OSC 133 event, valid only for the callback duration.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalSemanticPrompt
    {
        /// <summary>Size.</summary>
        public nuint Size;
        /// <summary>Kind.</summary>
        public GhosttySemanticPromptKind Kind;
        /// <summary>Prompt kind.</summary>
        public GhosttySemanticPromptPromptKind PromptKind;
        /// <summary>Has exit code.</summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool HasExitCode;
        /// <summary>Exit code.</summary>
        public int ExitCode;
        /// <summary>Command.</summary>
        public GhosttyString Command;
        /// <summary>Error.</summary>
        public GhosttyString Error;
    }

    /// <summary>Receives a native ProgramStatus event during terminal writes.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalProgramStatusCallback(nint terminal, nint userdata, GhosttyTerminalProgramStatus* report);

    /// <summary>Receives a native SemanticPrompt event during terminal writes.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalSemanticPromptCallback(nint terminal, nint userdata, GhosttyTerminalSemanticPrompt* prompt);

    /// <summary>Receives a native Reset event during terminal writes.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalResetCallback(nint terminal, nint userdata);

}
