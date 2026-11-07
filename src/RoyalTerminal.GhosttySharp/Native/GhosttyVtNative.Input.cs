// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    [Flags]
    public enum GhosttyKittyKeyFlags : byte
    {
        Disabled = 0,
        Disambiguate = 1 << 0,
        ReportEvents = 1 << 1,
        ReportAlternates = 1 << 2,
        ReportAll = 1 << 3,
        ReportAssociated = 1 << 4,
        All = Disambiguate | ReportEvents | ReportAlternates | ReportAll | ReportAssociated,
    }

    public enum GhosttyOptionAsAlt : int
    {
        False = 0,
        True = 1,
        Left = 2,
        Right = 3,
    }

    public enum GhosttyKeyEncoderOption : int
    {
        CursorKeyApplication = 1,
        KeypadKeyApplication = 2,
        IgnoreKeypadWithNumlock = 3,
        AltEscapePrefix = 4,
        ModifyOtherKeysState2 = 5,
        KittyFlags = 6,
        MacOsOptionAsAlt = 7,
        BackarrowKeyMode = 8,
        /// <summary>Upstream INVALID value.</summary>
        Invalid = 0,
    }

    public enum GhosttyMouseAction : int
    {
        Press = 0,
        Release = 1,
        Motion = 2,
    }

    public enum GhosttyMouseButtonId : int
    {
        Unknown = 0,
        Left = 1,
        Right = 2,
        Middle = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Eleven = 11,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyMousePosition
    {
        public float X;
        public float Y;
    }

    public enum GhosttyMouseTrackingMode : int
    {
        None = 0,
        X10 = 1,
        Normal = 2,
        Button = 3,
        Any = 4,
    }

    public enum GhosttyMouseFormat : int
    {
        X10 = 0,
        Utf8 = 1,
        Sgr = 2,
        Urxvt = 3,
        SgrPixels = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyMouseEncoderSize
    {
        public nuint Size;
        public uint ScreenWidth;
        public uint ScreenHeight;
        public uint CellWidth;
        public uint CellHeight;
        public uint PaddingTop;
        public uint PaddingBottom;
        public uint PaddingRight;
        public uint PaddingLeft;

        public static GhosttyMouseEncoderSize CreateSized()
        {
            return new GhosttyMouseEncoderSize
            {
                Size = (nuint)Marshal.SizeOf<GhosttyMouseEncoderSize>(),
            };
        }
    }

    public enum GhosttyMouseEncoderOption : int
    {
        Event = 1,
        Format = 2,
        Size = 3,
        AnyButtonPressed = 4,
        TrackLastCell = 5,
        /// <summary>Upstream INVALID value.</summary>
        Invalid = 0,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventNew(nint allocator, out nint mouseEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MouseEventFree(nint mouseEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_set_action")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventSetAction(nint mouseEvent, GhosttyMouseAction action);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_get_action")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyMouseAction MouseEventGetAction(nint mouseEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_set_button")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventSetButton(nint mouseEvent, GhosttyMouseButtonId button);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_clear_button")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventClearButton(nint mouseEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_get_button")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventGetButton(nint mouseEvent, out GhosttyMouseButtonId button);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_set_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventSetMods(nint mouseEvent, GhosttyVtMods mods);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_get_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyVtMods MouseEventGetMods(nint mouseEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_set_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEventSetPosition(nint mouseEvent, GhosttyMousePosition position);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_event_get_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyMousePosition MouseEventGetPosition(nint mouseEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_encoder_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_encoder_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void MouseEncoderFree(nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_encoder_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult MouseEncoderSetopt(nint encoder, GhosttyMouseEncoderOption option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_encoder_set_from_terminal")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_encoder_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult MouseEncoderReset(nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_mouse_encoder_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult MouseEncoderEncode(
        nint encoder,
        nint mouseEvent,
        byte* output,
        nuint outputLength,
        out nuint written);
}
