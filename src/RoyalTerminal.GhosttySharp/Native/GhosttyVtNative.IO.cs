// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    /// <summary>Reads bytes synchronously into a Ghostty-owned destination buffer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public unsafe delegate bool GhosttyReaderCallback(
        nint userdata,
        byte* buffer,
        nuint capacity,
        nuint* outRead);

    /// <summary>Writes a complete byte slice synchronously to a managed destination.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public unsafe delegate bool GhosttyWriterCallback(nint userdata, byte* data, nuint length);

    /// <summary>Streams the requested MIME representation to a Ghostty writer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public delegate bool GhosttyMimeReaderCallback(
        nint userdata,
        GhosttyString mime,
        GhosttyWriter writer);

    /// <summary>Native byte-source callback and context.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GhosttyReader
    {
        /// <summary>Creates a byte source from a callback function pointer and context.</summary>
        public GhosttyReader(nint read, nint userdata)
        {
            Read = read;
            Userdata = userdata;
        }

        /// <summary>Gets the unmanaged <see cref="GhosttyReaderCallback"/> pointer.</summary>
        public nint Read { get; }

        /// <summary>Gets the opaque callback context.</summary>
        public nint Userdata { get; }
    }

    /// <summary>Native byte-destination callback and context.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GhosttyWriter
    {
        /// <summary>Creates a byte destination from a callback function pointer and context.</summary>
        public GhosttyWriter(nint write, nint userdata)
        {
            Write = write;
            Userdata = userdata;
        }

        /// <summary>Gets the unmanaged <see cref="GhosttyWriterCallback"/> pointer.</summary>
        public nint Write { get; }

        /// <summary>Gets the opaque callback context.</summary>
        public nint Userdata { get; }
    }

    /// <summary>Native MIME-aware byte-source callback and context.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GhosttyMimeReader
    {
        /// <summary>Creates a MIME source from a callback function pointer and context.</summary>
        public GhosttyMimeReader(nint read, nint userdata)
        {
            Read = read;
            Userdata = userdata;
        }

        /// <summary>Gets the unmanaged <see cref="GhosttyMimeReaderCallback"/> pointer.</summary>
        public nint Read { get; }

        /// <summary>Gets the opaque callback context.</summary>
        public nint Userdata { get; }
    }
}
