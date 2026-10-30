// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Parser boundaries at which a bounded write may stop.</summary>
    [Flags]
    public enum GhosttyTerminalBoundary : int
    {
        /// <summary>Parser ground state, including immediately before consuming input.</summary>
        Ground = 1 << 0,
        /// <summary>APC introducer consumed.</summary>
        ApcStart = 1 << 1,
        /// <summary>APC ending byte consumed.</summary>
        ApcEnd = 1 << 2,
        /// <summary>DCS introducer consumed, before its header.</summary>
        DcsStart = 1 << 3,
        /// <summary>DCS ending byte consumed.</summary>
        DcsEnd = 1 << 4,
        /// <summary>OSC introducer consumed.</summary>
        OscStart = 1 << 5,
        /// <summary>OSC ending byte consumed.</summary>
        OscEnd = 1 << 6,
    }

    /// <summary>Control string associated with a parser boundary.</summary>
    public enum GhosttyTerminalStringKind : int
    {
        /// <summary>Application program command.</summary>
        Apc = 0,
        /// <summary>Device control string.</summary>
        Dcs = 1,
        /// <summary>Operating system command.</summary>
        Osc = 2,
    }

    /// <summary>Encoding of a control string introducer.</summary>
    public enum GhosttyTerminalStringIntroducer : int
    {
        /// <summary>Two-byte ESC introducer.</summary>
        Esc = 0,
        /// <summary>One-byte C1 introducer.</summary>
        C1 = 1,
    }

    /// <summary>How a control string ended.</summary>
    public enum GhosttyTerminalStringEnd : int
    {
        /// <summary>ESC was consumed; the following backslash remains unread.</summary>
        StEsc = 0,
        /// <summary>C1 ST was consumed.</summary>
        StC1 = 1,
        /// <summary>CAN or SUB cancelled the string.</summary>
        Cancelled = 2,
        /// <summary>Another control sequence aborted the string.</summary>
        Aborted = 3,
        /// <summary>BEL terminated an OSC string.</summary>
        Bel = 4,
    }

    /// <summary>Result of a bounded parser write; string fields apply only to reached string flags.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalWriteStop
    {
        /// <summary>Caller-initialized size in bytes.</summary>
        public nuint Size;
        /// <summary>Input bytes consumed, including the boundary byte.</summary>
        public nuint Consumed;
        /// <summary>Requested boundary flags reached by this write.</summary>
        public uint Reached;
        /// <summary>String kind, or the ended kind when one byte ends and starts strings.</summary>
        public GhosttyTerminalStringKind Kind;
        /// <summary>Encoding of a reached start boundary.</summary>
        public GhosttyTerminalStringIntroducer Introducer;
        /// <summary>Reason for a reached end boundary.</summary>
        public GhosttyTerminalStringEnd End;
        /// <summary>Byte that introduced or ended the string.</summary>
        public byte Byte;

        /// <summary>Creates an initialized native result structure.</summary>
        public static unsafe GhosttyTerminalWriteStop CreateSized() => new()
        {
            Size = (nuint)sizeof(GhosttyTerminalWriteStop),
        };
    }

    /// <summary>Writes through the first requested parser boundary, or consumes the whole input.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_vt_write_until")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalVtWriteUntil(
        nint terminal, byte* data, nuint length, uint boundaries, GhosttyTerminalWriteStop* stop);

    /// <summary>Compatibility wrapper for a ground-only bounded write.</summary>
    public static unsafe GhosttyResult TerminalVtWriteUntilGround(
        nint terminal, byte* data, nuint length, out nuint consumed)
    {
        GhosttyTerminalWriteStop stop = GhosttyTerminalWriteStop.CreateSized();
        GhosttyResult result = TerminalVtWriteUntil(terminal, data, length,
            (uint)GhosttyTerminalBoundary.Ground, &stop);
        consumed = stop.Consumed;
        return result;
    }
}
