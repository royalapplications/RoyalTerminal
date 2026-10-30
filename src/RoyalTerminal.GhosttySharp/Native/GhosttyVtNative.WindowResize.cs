// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Borrowed CSI 8 t callback. Zero dimensions preserve their current size. Never throw or re-enter native code.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RoyalWindowResizeCallback(nint terminal, nint userdata, ushort rows, ushort columns);

    /// <summary>Kind of host resize request. Ignore unknown future values.</summary>
    public enum GhosttyTerminalResizeWindowTag : int
    {
        /// <summary>Resize the text area to a number of rows and columns.</summary>
        Grid = 0,
    }

    /// <summary>Requested text grid size. A zero dimension preserves its current value.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalResizeWindowGrid
    {
        /// <summary>Requested rows, or zero to preserve the current height.</summary>
        public ushort Rows;
        /// <summary>Requested columns, or zero to preserve the current width.</summary>
        public ushort Columns;
    }

    /// <summary>Tagged window resize payload, aligned for the native ABI.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GhosttyTerminalResizeWindowValue
    {
        /// <summary>Grid size when the request tag is Grid.</summary>
        [FieldOffset(0)]
        public GhosttyTerminalResizeWindowGrid Grid;
        [FieldOffset(0)]
        private ulong _padding;
    }

    /// <summary>Borrowed host resize request; processing does not resize the terminal itself.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyTerminalResizeWindow
    {
        /// <summary>Discriminator of the request payload.</summary>
        public GhosttyTerminalResizeWindowTag Tag;
        /// <summary>Requested size.</summary>
        public GhosttyTerminalResizeWindowValue Value;
    }

    /// <summary>Official synchronous callback. The request is borrowed; never throw or re-enter the terminal.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GhosttyTerminalResizeWindowCallback(
        nint terminal, nint userdata, GhosttyTerminalResizeWindow* request);
}
