// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

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
}
