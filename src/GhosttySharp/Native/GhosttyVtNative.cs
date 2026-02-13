// Licensed under the MIT License.
// GhosttySharp — P/Invoke bindings for libghostty-vt (VT parsing library).

using System.Runtime.InteropServices;

namespace GhosttySharp.Native;

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
        Success = 0,
        OutOfMemory = -1,
        InvalidValue = -2,
    }

    // ──────────────────────────── Key Action ────────────────────────

    public enum GhosttyVtKeyAction : int
    {
        Release = 0,
        Press = 1,
        Repeat = 2,
    }

    // ──────────────────────────── Key Codes ─────────────────────────

    public enum GhosttyVtKey : int
    {
        Unidentified = 0,
        Backquote, Backslash, BracketLeft, BracketRight, Comma,
        Digit0, Digit1, Digit2, Digit3, Digit4,
        Digit5, Digit6, Digit7, Digit8, Digit9,
        Equal, IntlBackslash, IntlRo, IntlYen,
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Minus, Period, Quote, Semicolon, Slash,
        AltLeft, AltRight, Backspace, CapsLock, ContextMenu,
        ControlLeft, ControlRight, Enter, MetaLeft, MetaRight,
        ShiftLeft, ShiftRight, Space, Tab, Convert, KanaMode, NonConvert,
        Delete, End, Help, Home, Insert, PageDown, PageUp,
        ArrowDown, ArrowLeft, ArrowRight, ArrowUp,
    }

    // ──────────────────────────── Mods ───────────────────────────────

    [Flags]
    public enum GhosttyVtMods : ushort
    {
        None = 0,
        Shift = 1 << 0,
        Ctrl = 1 << 1,
        Alt = 1 << 2,
        Super = 1 << 3,
        CapsLock = 1 << 4,
        NumLock = 1 << 5,
    }

    // ──────────────────────────── OSC ────────────────────────────────

    public enum GhosttyOscCommandType : int
    {
        Invalid = 0,
        ChangeWindowTitle = 1,
        ChangeWindowIcon = 2,
        SemanticPrompt = 3,
        ClipboardContents = 4,
        ReportPwd = 5,
        MouseShape = 6,
        ColorOperation = 7,
        KittyColorProtocol = 8,
        ShowDesktopNotification = 9,
        HyperlinkStart = 10,
        HyperlinkEnd = 11,
    }

    public enum GhosttyOscCommandData : int
    {
        Invalid = 0,
        ChangeWindowTitleStr = 1,
    }

    // ──────────────────────────── SGR ────────────────────────────────

    public enum GhosttySgrAttributeTag : int
    {
        Unset = 0,
        Unknown = 1,
        Bold = 2,
        ResetBold = 3,
        Italic = 4,
        ResetItalic = 5,
        Faint = 6,
        Underline = 7,
        ResetUnderline = 8,
        UnderlineColor = 9,
        UnderlineColor256 = 10,
        ResetUnderlineColor = 11,
        Overline = 12,
        ResetOverline = 13,
        Blink = 14,
        ResetBlink = 15,
        Inverse = 16,
        ResetInverse = 17,
        Invisible = 18,
        ResetInvisible = 19,
        Strikethrough = 20,
        ResetStrikethrough = 21,
        DirectColorFg = 22,
        DirectColorBg = 23,
        Bg8 = 24,
        Fg8 = 25,
        ResetFg = 26,
        ResetBg = 27,
        BrightBg8 = 28,
        BrightFg8 = 29,
        Bg256 = 30,
        Fg256 = 31,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyColorRgb
    {
        public byte R;
        public byte G;
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
    [return: MarshalAs(UnmanagedType.U1)]
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

    // ────────────────────── Paste Functions ─────────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_paste_is_safe")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static unsafe partial bool PasteIsSafe(byte* data, nuint len);

    // ────────────────────── Color Functions ─────────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_color_rgb_get")]
    public static unsafe partial void ColorRgbGet(GhosttyColorRgb color, byte* r, byte* g, byte* b);
}
