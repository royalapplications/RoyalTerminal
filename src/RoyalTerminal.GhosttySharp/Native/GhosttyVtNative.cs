// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp — P/Invoke bindings for libghostty-vt (VT parsing library).

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

/// <summary>
/// P/Invoke declarations for libghostty-vt — the Ghostty virtual terminal parsing library.
/// Covers OSC parser, SGR parser, key encoder, and paste safety utilities.
/// </summary>
public static partial class GhosttyVtNative
{
    private const string LibName = "ghostty-vt";

    // ──────────────────────────── Result ─────────────────────────────

    /// <summary>GhosttyResult codes.</summary>
    public enum GhosttyResult : int
    {
        /// <summary><c>Success</c> enum value.</summary>
        Success = 0,
        /// <summary><c>OutOfMemory</c> enum value.</summary>
        OutOfMemory = -1,
        /// <summary><c>InvalidValue</c> enum value.</summary>
        InvalidValue = -2,
        /// <summary><c>OutOfSpace</c> enum value.</summary>
        OutOfSpace = -3,
        /// <summary><c>NoValue</c> enum value.</summary>
        NoValue = -4,
    }

    // ──────────────────────────── Key Action ────────────────────────

    /// <summary>Key action for a VT key event.</summary>
    public enum GhosttyVtKeyAction : int
    {
        /// <summary><c>Release</c> enum value.</summary>
        Release = 0,
        /// <summary><c>Press</c> enum value.</summary>
        Press = 1,
        /// <summary><c>Repeat</c> enum value.</summary>
        Repeat = 2,
    }

    // ──────────────────────────── Key Codes ─────────────────────────

    /// <summary>
    /// VT key code identifiers used by the key encoder.
    /// Member names mirror <see cref="GhosttyKey"/> and the upstream W3C key naming.
    /// </summary>
    public enum GhosttyVtKey : int
    {
        /// <summary><c>Unidentified</c> enum value.</summary>
        Unidentified = 0,
        /// <summary><c>Backquote</c> enum value.</summary>
        Backquote,
        /// <summary><c>Backslash</c> enum value.</summary>
        Backslash,
        /// <summary><c>BracketLeft</c> enum value.</summary>
        BracketLeft,
        /// <summary><c>BracketRight</c> enum value.</summary>
        BracketRight,
        /// <summary><c>Comma</c> enum value.</summary>
        Comma,
        /// <summary><c>Digit0</c> enum value.</summary>
        Digit0,
        /// <summary><c>Digit1</c> enum value.</summary>
        Digit1,
        /// <summary><c>Digit2</c> enum value.</summary>
        Digit2,
        /// <summary><c>Digit3</c> enum value.</summary>
        Digit3,
        /// <summary><c>Digit4</c> enum value.</summary>
        Digit4,
        /// <summary><c>Digit5</c> enum value.</summary>
        Digit5,
        /// <summary><c>Digit6</c> enum value.</summary>
        Digit6,
        /// <summary><c>Digit7</c> enum value.</summary>
        Digit7,
        /// <summary><c>Digit8</c> enum value.</summary>
        Digit8,
        /// <summary><c>Digit9</c> enum value.</summary>
        Digit9,
        /// <summary><c>Equal</c> enum value.</summary>
        Equal,
        /// <summary><c>IntlBackslash</c> enum value.</summary>
        IntlBackslash,
        /// <summary><c>IntlRo</c> enum value.</summary>
        IntlRo,
        /// <summary><c>IntlYen</c> enum value.</summary>
        IntlYen,
        /// <summary><c>A</c> enum value.</summary>
        A,
        /// <summary><c>B</c> enum value.</summary>
        B,
        /// <summary><c>C</c> enum value.</summary>
        C,
        /// <summary><c>D</c> enum value.</summary>
        D,
        /// <summary><c>E</c> enum value.</summary>
        E,
        /// <summary><c>F</c> enum value.</summary>
        F,
        /// <summary><c>G</c> enum value.</summary>
        G,
        /// <summary><c>H</c> enum value.</summary>
        H,
        /// <summary><c>I</c> enum value.</summary>
        I,
        /// <summary><c>J</c> enum value.</summary>
        J,
        /// <summary><c>K</c> enum value.</summary>
        K,
        /// <summary><c>L</c> enum value.</summary>
        L,
        /// <summary><c>M</c> enum value.</summary>
        M,
        /// <summary><c>N</c> enum value.</summary>
        N,
        /// <summary><c>O</c> enum value.</summary>
        O,
        /// <summary><c>P</c> enum value.</summary>
        P,
        /// <summary><c>Q</c> enum value.</summary>
        Q,
        /// <summary><c>R</c> enum value.</summary>
        R,
        /// <summary><c>S</c> enum value.</summary>
        S,
        /// <summary><c>T</c> enum value.</summary>
        T,
        /// <summary><c>U</c> enum value.</summary>
        U,
        /// <summary><c>V</c> enum value.</summary>
        V,
        /// <summary><c>W</c> enum value.</summary>
        W,
        /// <summary><c>X</c> enum value.</summary>
        X,
        /// <summary><c>Y</c> enum value.</summary>
        Y,
        /// <summary><c>Z</c> enum value.</summary>
        Z,
        /// <summary><c>Minus</c> enum value.</summary>
        Minus,
        /// <summary><c>Period</c> enum value.</summary>
        Period,
        /// <summary><c>Quote</c> enum value.</summary>
        Quote,
        /// <summary><c>Semicolon</c> enum value.</summary>
        Semicolon,
        /// <summary><c>Slash</c> enum value.</summary>
        Slash,
        /// <summary><c>AltLeft</c> enum value.</summary>
        AltLeft,
        /// <summary><c>AltRight</c> enum value.</summary>
        AltRight,
        /// <summary><c>Backspace</c> enum value.</summary>
        Backspace,
        /// <summary><c>CapsLock</c> enum value.</summary>
        CapsLock,
        /// <summary><c>ContextMenu</c> enum value.</summary>
        ContextMenu,
        /// <summary><c>ControlLeft</c> enum value.</summary>
        ControlLeft,
        /// <summary><c>ControlRight</c> enum value.</summary>
        ControlRight,
        /// <summary><c>Enter</c> enum value.</summary>
        Enter,
        /// <summary><c>MetaLeft</c> enum value.</summary>
        MetaLeft,
        /// <summary><c>MetaRight</c> enum value.</summary>
        MetaRight,
        /// <summary><c>ShiftLeft</c> enum value.</summary>
        ShiftLeft,
        /// <summary><c>ShiftRight</c> enum value.</summary>
        ShiftRight,
        /// <summary><c>Space</c> enum value.</summary>
        Space,
        /// <summary><c>Tab</c> enum value.</summary>
        Tab,
        /// <summary><c>Convert</c> enum value.</summary>
        Convert,
        /// <summary><c>KanaMode</c> enum value.</summary>
        KanaMode,
        /// <summary><c>NonConvert</c> enum value.</summary>
        NonConvert,
        /// <summary><c>Delete</c> enum value.</summary>
        Delete,
        /// <summary><c>End</c> enum value.</summary>
        End,
        /// <summary><c>Help</c> enum value.</summary>
        Help,
        /// <summary><c>Home</c> enum value.</summary>
        Home,
        /// <summary><c>Insert</c> enum value.</summary>
        Insert,
        /// <summary><c>PageDown</c> enum value.</summary>
        PageDown,
        /// <summary><c>PageUp</c> enum value.</summary>
        PageUp,
        /// <summary><c>ArrowDown</c> enum value.</summary>
        ArrowDown,
        /// <summary><c>ArrowLeft</c> enum value.</summary>
        ArrowLeft,
        /// <summary><c>ArrowRight</c> enum value.</summary>
        ArrowRight,
        /// <summary><c>ArrowUp</c> enum value.</summary>
        ArrowUp,
        /// <summary><c>NumLock</c> enum value.</summary>
        NumLock,
        /// <summary><c>Numpad0</c> enum value.</summary>
        Numpad0,
        /// <summary><c>Numpad1</c> enum value.</summary>
        Numpad1,
        /// <summary><c>Numpad2</c> enum value.</summary>
        Numpad2,
        /// <summary><c>Numpad3</c> enum value.</summary>
        Numpad3,
        /// <summary><c>Numpad4</c> enum value.</summary>
        Numpad4,
        /// <summary><c>Numpad5</c> enum value.</summary>
        Numpad5,
        /// <summary><c>Numpad6</c> enum value.</summary>
        Numpad6,
        /// <summary><c>Numpad7</c> enum value.</summary>
        Numpad7,
        /// <summary><c>Numpad8</c> enum value.</summary>
        Numpad8,
        /// <summary><c>Numpad9</c> enum value.</summary>
        Numpad9,
        /// <summary><c>NumpadAdd</c> enum value.</summary>
        NumpadAdd,
        /// <summary><c>NumpadBackspace</c> enum value.</summary>
        NumpadBackspace,
        /// <summary><c>NumpadClear</c> enum value.</summary>
        NumpadClear,
        /// <summary><c>NumpadClearEntry</c> enum value.</summary>
        NumpadClearEntry,
        /// <summary><c>NumpadComma</c> enum value.</summary>
        NumpadComma,
        /// <summary><c>NumpadDecimal</c> enum value.</summary>
        NumpadDecimal,
        /// <summary><c>NumpadDivide</c> enum value.</summary>
        NumpadDivide,
        /// <summary><c>NumpadEnter</c> enum value.</summary>
        NumpadEnter,
        /// <summary><c>NumpadEqual</c> enum value.</summary>
        NumpadEqual,
        /// <summary><c>NumpadMemoryAdd</c> enum value.</summary>
        NumpadMemoryAdd,
        /// <summary><c>NumpadMemoryClear</c> enum value.</summary>
        NumpadMemoryClear,
        /// <summary><c>NumpadMemoryRecall</c> enum value.</summary>
        NumpadMemoryRecall,
        /// <summary><c>NumpadMemoryStore</c> enum value.</summary>
        NumpadMemoryStore,
        /// <summary><c>NumpadMemorySubtract</c> enum value.</summary>
        NumpadMemorySubtract,
        /// <summary><c>NumpadMultiply</c> enum value.</summary>
        NumpadMultiply,
        /// <summary><c>NumpadParenLeft</c> enum value.</summary>
        NumpadParenLeft,
        /// <summary><c>NumpadParenRight</c> enum value.</summary>
        NumpadParenRight,
        /// <summary><c>NumpadSubtract</c> enum value.</summary>
        NumpadSubtract,
        /// <summary><c>NumpadSeparator</c> enum value.</summary>
        NumpadSeparator,
        /// <summary><c>NumpadUp</c> enum value.</summary>
        NumpadUp,
        /// <summary><c>NumpadDown</c> enum value.</summary>
        NumpadDown,
        /// <summary><c>NumpadRight</c> enum value.</summary>
        NumpadRight,
        /// <summary><c>NumpadLeft</c> enum value.</summary>
        NumpadLeft,
        /// <summary><c>NumpadBegin</c> enum value.</summary>
        NumpadBegin,
        /// <summary><c>NumpadHome</c> enum value.</summary>
        NumpadHome,
        /// <summary><c>NumpadEnd</c> enum value.</summary>
        NumpadEnd,
        /// <summary><c>NumpadInsert</c> enum value.</summary>
        NumpadInsert,
        /// <summary><c>NumpadDelete</c> enum value.</summary>
        NumpadDelete,
        /// <summary><c>NumpadPageUp</c> enum value.</summary>
        NumpadPageUp,
        /// <summary><c>NumpadPageDown</c> enum value.</summary>
        NumpadPageDown,
        /// <summary><c>Escape</c> enum value.</summary>
        Escape,
        /// <summary><c>F1</c> enum value.</summary>
        F1,
        /// <summary><c>F2</c> enum value.</summary>
        F2,
        /// <summary><c>F3</c> enum value.</summary>
        F3,
        /// <summary><c>F4</c> enum value.</summary>
        F4,
        /// <summary><c>F5</c> enum value.</summary>
        F5,
        /// <summary><c>F6</c> enum value.</summary>
        F6,
        /// <summary><c>F7</c> enum value.</summary>
        F7,
        /// <summary><c>F8</c> enum value.</summary>
        F8,
        /// <summary><c>F9</c> enum value.</summary>
        F9,
        /// <summary><c>F10</c> enum value.</summary>
        F10,
        /// <summary><c>F11</c> enum value.</summary>
        F11,
        /// <summary><c>F12</c> enum value.</summary>
        F12,
        /// <summary><c>F13</c> enum value.</summary>
        F13,
        /// <summary><c>F14</c> enum value.</summary>
        F14,
        /// <summary><c>F15</c> enum value.</summary>
        F15,
        /// <summary><c>F16</c> enum value.</summary>
        F16,
        /// <summary><c>F17</c> enum value.</summary>
        F17,
        /// <summary><c>F18</c> enum value.</summary>
        F18,
        /// <summary><c>F19</c> enum value.</summary>
        F19,
        /// <summary><c>F20</c> enum value.</summary>
        F20,
    }

    // ──────────────────────────── Mods ───────────────────────────────

    /// <summary>Modifier flags for VT key events.</summary>
    [Flags]
    public enum GhosttyVtMods : ushort
    {
        /// <summary><c>None</c> enum value.</summary>
        None = 0,
        /// <summary><c>Shift</c> enum value.</summary>
        Shift = 1 << 0,
        /// <summary><c>Ctrl</c> enum value.</summary>
        Ctrl = 1 << 1,
        /// <summary><c>Alt</c> enum value.</summary>
        Alt = 1 << 2,
        /// <summary><c>Super</c> enum value.</summary>
        Super = 1 << 3,
        /// <summary><c>CapsLock</c> enum value.</summary>
        CapsLock = 1 << 4,
        /// <summary><c>NumLock</c> enum value.</summary>
        NumLock = 1 << 5,
    }

    // ──────────────────────────── OSC ────────────────────────────────

    /// <summary>OSC command type decoded by the OSC parser.</summary>
    public enum GhosttyOscCommandType : int
    {
        /// <summary><c>Invalid</c> enum value.</summary>
        Invalid = 0,
        /// <summary><c>ChangeWindowTitle</c> enum value.</summary>
        ChangeWindowTitle = 1,
        /// <summary><c>ChangeWindowIcon</c> enum value.</summary>
        ChangeWindowIcon = 2,
        /// <summary><c>SemanticPrompt</c> enum value.</summary>
        SemanticPrompt = 3,
        /// <summary><c>ClipboardContents</c> enum value.</summary>
        ClipboardContents = 4,
        /// <summary><c>ReportPwd</c> enum value.</summary>
        ReportPwd = 5,
        /// <summary><c>MouseShape</c> enum value.</summary>
        MouseShape = 6,
        /// <summary><c>ColorOperation</c> enum value.</summary>
        ColorOperation = 7,
        /// <summary><c>KittyColorProtocol</c> enum value.</summary>
        KittyColorProtocol = 8,
        /// <summary><c>ShowDesktopNotification</c> enum value.</summary>
        ShowDesktopNotification = 9,
        /// <summary><c>HyperlinkStart</c> enum value.</summary>
        HyperlinkStart = 10,
        /// <summary><c>HyperlinkEnd</c> enum value.</summary>
        HyperlinkEnd = 11,
    }

    /// <summary>Data selector when reading typed OSC command data.</summary>
    public enum GhosttyOscCommandData : int
    {
        /// <summary><c>Invalid</c> enum value.</summary>
        Invalid = 0,
        /// <summary><c>ChangeWindowTitleStr</c> enum value.</summary>
        ChangeWindowTitleStr = 1,
    }

    // ──────────────────────────── SGR ────────────────────────────────

    /// <summary>SGR attribute tag decoded by the SGR parser.</summary>
    public enum GhosttySgrAttributeTag : int
    {
        /// <summary><c>Unset</c> enum value.</summary>
        Unset = 0,
        /// <summary><c>Unknown</c> enum value.</summary>
        Unknown = 1,
        /// <summary><c>Bold</c> enum value.</summary>
        Bold = 2,
        /// <summary><c>ResetBold</c> enum value.</summary>
        ResetBold = 3,
        /// <summary><c>Italic</c> enum value.</summary>
        Italic = 4,
        /// <summary><c>ResetItalic</c> enum value.</summary>
        ResetItalic = 5,
        /// <summary><c>Faint</c> enum value.</summary>
        Faint = 6,
        /// <summary><c>Underline</c> enum value.</summary>
        Underline = 7,
        /// <summary><c>UnderlineColor</c> enum value.</summary>
        UnderlineColor = 8,
        /// <summary><c>UnderlineColor256</c> enum value.</summary>
        UnderlineColor256 = 9,
        /// <summary><c>ResetUnderlineColor</c> enum value.</summary>
        ResetUnderlineColor = 10,
        /// <summary><c>Overline</c> enum value.</summary>
        Overline = 11,
        /// <summary><c>ResetOverline</c> enum value.</summary>
        ResetOverline = 12,
        /// <summary><c>Blink</c> enum value.</summary>
        Blink = 13,
        /// <summary><c>ResetBlink</c> enum value.</summary>
        ResetBlink = 14,
        /// <summary><c>Inverse</c> enum value.</summary>
        Inverse = 15,
        /// <summary><c>ResetInverse</c> enum value.</summary>
        ResetInverse = 16,
        /// <summary><c>Invisible</c> enum value.</summary>
        Invisible = 17,
        /// <summary><c>ResetInvisible</c> enum value.</summary>
        ResetInvisible = 18,
        /// <summary><c>Strikethrough</c> enum value.</summary>
        Strikethrough = 19,
        /// <summary><c>ResetStrikethrough</c> enum value.</summary>
        ResetStrikethrough = 20,
        /// <summary><c>DirectColorFg</c> enum value.</summary>
        DirectColorFg = 21,
        /// <summary><c>DirectColorBg</c> enum value.</summary>
        DirectColorBg = 22,
        /// <summary><c>Bg8</c> enum value.</summary>
        Bg8 = 23,
        /// <summary><c>Fg8</c> enum value.</summary>
        Fg8 = 24,
        /// <summary><c>ResetFg</c> enum value.</summary>
        ResetFg = 25,
        /// <summary><c>ResetBg</c> enum value.</summary>
        ResetBg = 26,
        /// <summary><c>BrightBg8</c> enum value.</summary>
        BrightBg8 = 27,
        /// <summary><c>BrightFg8</c> enum value.</summary>
        BrightFg8 = 28,
        /// <summary><c>Bg256</c> enum value.</summary>
        Bg256 = 29,
        /// <summary><c>Fg256</c> enum value.</summary>
        Fg256 = 30,
    }

    /// <summary>Underline style payload decoded by the SGR parser.</summary>
    public enum GhosttySgrUnderline : int
    {
        None = 0,
        Single = 1,
        Double = 2,
        Curly = 3,
        Dotted = 4,
        Dashed = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttySgrUnknown
    {
        public nint FullPtr;
        public nuint FullLength;
        public nint PartialPtr;
        public nuint PartialLength;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct GhosttySgrAttributeValue
    {
        [FieldOffset(0)]
        public GhosttySgrUnknown Unknown;

        [FieldOffset(0)]
        public GhosttySgrUnderline Underline;

        [FieldOffset(0)]
        public GhosttyColorRgb UnderlineColor;

        [FieldOffset(0)]
        public byte UnderlineColor256;

        [FieldOffset(0)]
        public GhosttyColorRgb DirectColorForeground;

        [FieldOffset(0)]
        public GhosttyColorRgb DirectColorBackground;

        [FieldOffset(0)]
        public byte Background8;

        [FieldOffset(0)]
        public byte Foreground8;

        [FieldOffset(0)]
        public byte BrightBackground8;

        [FieldOffset(0)]
        public byte BrightForeground8;

        [FieldOffset(0)]
        public byte Background256;

        [FieldOffset(0)]
        public byte Foreground256;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct GhosttySgrAttribute
    {
        [FieldOffset(0)]
        public GhosttySgrAttributeTag Tag;

        [FieldOffset(8)]
        public GhosttySgrAttributeValue Value;
    }

    /// <summary>RGB color payload used by color parser helpers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyColorRgb
    {
        /// <summary>Native field <c>R</c>.</summary>
        public byte R;
        /// <summary>Native field <c>G</c>.</summary>
        public byte G;
        /// <summary>Native field <c>B</c>.</summary>
        public byte B;
    }

    // ────────────────────── Key Event Functions ─────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_new")]
    public static partial GhosttyResult KeyEventNew(nint allocator, out nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_free")]
    public static partial void KeyEventFree(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_action")]
    public static partial void KeyEventSetAction(nint keyEvent, GhosttyVtKeyAction action);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_action")]
    public static partial GhosttyVtKeyAction KeyEventGetAction(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_key")]
    public static partial void KeyEventSetKey(nint keyEvent, GhosttyVtKey key);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_key")]
    public static partial GhosttyVtKey KeyEventGetKey(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_mods")]
    public static partial void KeyEventSetMods(nint keyEvent, GhosttyVtMods mods);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_mods")]
    public static partial GhosttyVtMods KeyEventGetMods(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_consumed_mods")]
    public static partial void KeyEventSetConsumedMods(nint keyEvent, GhosttyVtMods consumedMods);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_consumed_mods")]
    public static partial GhosttyVtMods KeyEventGetConsumedMods(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_composing")]
    public static partial void KeyEventSetComposing(nint keyEvent, [MarshalAs(UnmanagedType.U1)] bool composing);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_composing")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool KeyEventGetComposing(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_utf8")]
    public static unsafe partial void KeyEventSetUtf8(nint keyEvent, byte* utf8, nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_utf8")]
    public static unsafe partial byte* KeyEventGetUtf8(nint keyEvent, out nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_unshifted_codepoint")]
    public static partial void KeyEventSetUnshiftedCodepoint(nint keyEvent, uint codepoint);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_unshifted_codepoint")]
    public static partial uint KeyEventGetUnshiftedCodepoint(nint keyEvent);

    // ────────────────────── Key Encoder Functions ───────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_new")]
    public static partial GhosttyResult KeyEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_free")]
    public static partial void KeyEncoderFree(nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_setopt")]
    public static unsafe partial void KeyEncoderSetopt(nint encoder, int option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_setopt_from_terminal")]
    public static partial void KeyEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_encode")]
    public static unsafe partial GhosttyResult KeyEncoderEncode(
        nint encoder, nint keyEvent,
        byte* outBuf, nuint outBufSize, out nuint outLen);

    // ────────────────────── OSC Parser Functions ────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_new")]
    public static partial GhosttyResult OscNew(nint allocator, out nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_free")]
    public static partial void OscFree(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_reset")]
    public static partial void OscReset(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_next")]
    public static partial void OscNext(nint parser, byte b);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_end")]
    public static partial nint OscEnd(nint parser, byte terminator);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_command_type")]
    public static partial GhosttyOscCommandType OscCommandType(nint command);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_command_data")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static unsafe partial bool OscCommandData(nint command, GhosttyOscCommandData dataType, void* outPtr);

    // ────────────────────── SGR Parser Functions ────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_new")]
    public static partial GhosttyResult SgrNew(nint allocator, out nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_free")]
    public static partial void SgrFree(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_reset")]
    public static partial void SgrReset(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_set_params")]
    public static unsafe partial GhosttyResult SgrSetParams(nint parser, ushort* param, byte* separators, nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_next")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static unsafe partial bool SgrNext(nint parser, void* attr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_unknown_full")]
    public static unsafe partial nuint SgrUnknownFull(GhosttySgrUnknown unknown, ushort** ptr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_unknown_partial")]
    public static unsafe partial nuint SgrUnknownPartial(GhosttySgrUnknown unknown, ushort** ptr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_attribute_tag")]
    public static partial GhosttySgrAttributeTag SgrAttributeTag(GhosttySgrAttribute attr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_attribute_value")]
    public static unsafe partial GhosttySgrAttributeValue* SgrAttributeValue(GhosttySgrAttribute* attr);

    // ────────────────────── Paste Functions ─────────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_paste_is_safe")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static unsafe partial bool PasteIsSafe(byte* data, nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_paste_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult PasteEncode(
        byte* data,
        nuint dataLen,
        [MarshalAs(UnmanagedType.U1)] bool bracketed,
        byte* buffer,
        nuint bufferLen,
        out nuint written);

    // ────────────────────── Color Functions ─────────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_color_rgb_get")]
    public static unsafe partial void ColorRgbGet(GhosttyColorRgb color, byte* r, byte* g, byte* b);

    [LibraryImport(LibName, EntryPoint = "ghostty_type_json")]
    public static partial nint TypeJson();
}
