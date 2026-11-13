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
        /// <summary>A synchronous reader or writer callback reported an I/O failure.</summary>
        IoError = -5,
        /// <summary>An operation exceeded an explicit size or accounting limit.</summary>
        LimitExceeded = -6,
        /// <summary>The operation was valid but rejected by terminal policy.</summary>
        Rejected = -7,
        /// <summary>Upstream UNSUPPORTED value.</summary>
        Unsupported = -8,
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
        Backquote = 1,
        /// <summary><c>Backslash</c> enum value.</summary>
        Backslash = 2,
        /// <summary><c>BracketLeft</c> enum value.</summary>
        BracketLeft = 3,
        /// <summary><c>BracketRight</c> enum value.</summary>
        BracketRight = 4,
        /// <summary><c>Comma</c> enum value.</summary>
        Comma = 5,
        /// <summary><c>Digit0</c> enum value.</summary>
        Digit0 = 6,
        /// <summary><c>Digit1</c> enum value.</summary>
        Digit1 = 7,
        /// <summary><c>Digit2</c> enum value.</summary>
        Digit2 = 8,
        /// <summary><c>Digit3</c> enum value.</summary>
        Digit3 = 9,
        /// <summary><c>Digit4</c> enum value.</summary>
        Digit4 = 10,
        /// <summary><c>Digit5</c> enum value.</summary>
        Digit5 = 11,
        /// <summary><c>Digit6</c> enum value.</summary>
        Digit6 = 12,
        /// <summary><c>Digit7</c> enum value.</summary>
        Digit7 = 13,
        /// <summary><c>Digit8</c> enum value.</summary>
        Digit8 = 14,
        /// <summary><c>Digit9</c> enum value.</summary>
        Digit9 = 15,
        /// <summary><c>Equal</c> enum value.</summary>
        Equal = 16,
        /// <summary><c>IntlBackslash</c> enum value.</summary>
        IntlBackslash = 17,
        /// <summary><c>IntlRo</c> enum value.</summary>
        IntlRo = 18,
        /// <summary><c>IntlYen</c> enum value.</summary>
        IntlYen = 19,
        /// <summary><c>A</c> enum value.</summary>
        A = 20,
        /// <summary><c>B</c> enum value.</summary>
        B = 21,
        /// <summary><c>C</c> enum value.</summary>
        C = 22,
        /// <summary><c>D</c> enum value.</summary>
        D = 23,
        /// <summary><c>E</c> enum value.</summary>
        E = 24,
        /// <summary><c>F</c> enum value.</summary>
        F = 25,
        /// <summary><c>G</c> enum value.</summary>
        G = 26,
        /// <summary><c>H</c> enum value.</summary>
        H = 27,
        /// <summary><c>I</c> enum value.</summary>
        I = 28,
        /// <summary><c>J</c> enum value.</summary>
        J = 29,
        /// <summary><c>K</c> enum value.</summary>
        K = 30,
        /// <summary><c>L</c> enum value.</summary>
        L = 31,
        /// <summary><c>M</c> enum value.</summary>
        M = 32,
        /// <summary><c>N</c> enum value.</summary>
        N = 33,
        /// <summary><c>O</c> enum value.</summary>
        O = 34,
        /// <summary><c>P</c> enum value.</summary>
        P = 35,
        /// <summary><c>Q</c> enum value.</summary>
        Q = 36,
        /// <summary><c>R</c> enum value.</summary>
        R = 37,
        /// <summary><c>S</c> enum value.</summary>
        S = 38,
        /// <summary><c>T</c> enum value.</summary>
        T = 39,
        /// <summary><c>U</c> enum value.</summary>
        U = 40,
        /// <summary><c>V</c> enum value.</summary>
        V = 41,
        /// <summary><c>W</c> enum value.</summary>
        W = 42,
        /// <summary><c>X</c> enum value.</summary>
        X = 43,
        /// <summary><c>Y</c> enum value.</summary>
        Y = 44,
        /// <summary><c>Z</c> enum value.</summary>
        Z = 45,
        /// <summary><c>Minus</c> enum value.</summary>
        Minus = 46,
        /// <summary><c>Period</c> enum value.</summary>
        Period = 47,
        /// <summary><c>Quote</c> enum value.</summary>
        Quote = 48,
        /// <summary><c>Semicolon</c> enum value.</summary>
        Semicolon = 49,
        /// <summary><c>Slash</c> enum value.</summary>
        Slash = 50,
        /// <summary><c>AltLeft</c> enum value.</summary>
        AltLeft = 51,
        /// <summary><c>AltRight</c> enum value.</summary>
        AltRight = 52,
        /// <summary><c>Backspace</c> enum value.</summary>
        Backspace = 53,
        /// <summary><c>CapsLock</c> enum value.</summary>
        CapsLock = 54,
        /// <summary><c>ContextMenu</c> enum value.</summary>
        ContextMenu = 55,
        /// <summary><c>ControlLeft</c> enum value.</summary>
        ControlLeft = 56,
        /// <summary><c>ControlRight</c> enum value.</summary>
        ControlRight = 57,
        /// <summary><c>Enter</c> enum value.</summary>
        Enter = 58,
        /// <summary><c>MetaLeft</c> enum value.</summary>
        MetaLeft = 59,
        /// <summary><c>MetaRight</c> enum value.</summary>
        MetaRight = 60,
        /// <summary><c>ShiftLeft</c> enum value.</summary>
        ShiftLeft = 61,
        /// <summary><c>ShiftRight</c> enum value.</summary>
        ShiftRight = 62,
        /// <summary><c>Space</c> enum value.</summary>
        Space = 63,
        /// <summary><c>Tab</c> enum value.</summary>
        Tab = 64,
        /// <summary><c>Convert</c> enum value.</summary>
        Convert = 65,
        /// <summary><c>KanaMode</c> enum value.</summary>
        KanaMode = 66,
        /// <summary><c>NonConvert</c> enum value.</summary>
        NonConvert = 67,
        /// <summary><c>Delete</c> enum value.</summary>
        Delete = 68,
        /// <summary><c>End</c> enum value.</summary>
        End = 69,
        /// <summary><c>Help</c> enum value.</summary>
        Help = 70,
        /// <summary><c>Home</c> enum value.</summary>
        Home = 71,
        /// <summary><c>Insert</c> enum value.</summary>
        Insert = 72,
        /// <summary><c>PageDown</c> enum value.</summary>
        PageDown = 73,
        /// <summary><c>PageUp</c> enum value.</summary>
        PageUp = 74,
        /// <summary><c>ArrowDown</c> enum value.</summary>
        ArrowDown = 75,
        /// <summary><c>ArrowLeft</c> enum value.</summary>
        ArrowLeft = 76,
        /// <summary><c>ArrowRight</c> enum value.</summary>
        ArrowRight = 77,
        /// <summary><c>ArrowUp</c> enum value.</summary>
        ArrowUp = 78,
        /// <summary><c>NumLock</c> enum value.</summary>
        NumLock = 79,
        /// <summary><c>Numpad0</c> enum value.</summary>
        Numpad0 = 80,
        /// <summary><c>Numpad1</c> enum value.</summary>
        Numpad1 = 81,
        /// <summary><c>Numpad2</c> enum value.</summary>
        Numpad2 = 82,
        /// <summary><c>Numpad3</c> enum value.</summary>
        Numpad3 = 83,
        /// <summary><c>Numpad4</c> enum value.</summary>
        Numpad4 = 84,
        /// <summary><c>Numpad5</c> enum value.</summary>
        Numpad5 = 85,
        /// <summary><c>Numpad6</c> enum value.</summary>
        Numpad6 = 86,
        /// <summary><c>Numpad7</c> enum value.</summary>
        Numpad7 = 87,
        /// <summary><c>Numpad8</c> enum value.</summary>
        Numpad8 = 88,
        /// <summary><c>Numpad9</c> enum value.</summary>
        Numpad9 = 89,
        /// <summary><c>NumpadAdd</c> enum value.</summary>
        NumpadAdd = 90,
        /// <summary><c>NumpadBackspace</c> enum value.</summary>
        NumpadBackspace = 91,
        /// <summary><c>NumpadClear</c> enum value.</summary>
        NumpadClear = 92,
        /// <summary><c>NumpadClearEntry</c> enum value.</summary>
        NumpadClearEntry = 93,
        /// <summary><c>NumpadComma</c> enum value.</summary>
        NumpadComma = 94,
        /// <summary><c>NumpadDecimal</c> enum value.</summary>
        NumpadDecimal = 95,
        /// <summary><c>NumpadDivide</c> enum value.</summary>
        NumpadDivide = 96,
        /// <summary><c>NumpadEnter</c> enum value.</summary>
        NumpadEnter = 97,
        /// <summary><c>NumpadEqual</c> enum value.</summary>
        NumpadEqual = 98,
        /// <summary><c>NumpadMemoryAdd</c> enum value.</summary>
        NumpadMemoryAdd = 99,
        /// <summary><c>NumpadMemoryClear</c> enum value.</summary>
        NumpadMemoryClear = 100,
        /// <summary><c>NumpadMemoryRecall</c> enum value.</summary>
        NumpadMemoryRecall = 101,
        /// <summary><c>NumpadMemoryStore</c> enum value.</summary>
        NumpadMemoryStore = 102,
        /// <summary><c>NumpadMemorySubtract</c> enum value.</summary>
        NumpadMemorySubtract = 103,
        /// <summary><c>NumpadMultiply</c> enum value.</summary>
        NumpadMultiply = 104,
        /// <summary><c>NumpadParenLeft</c> enum value.</summary>
        NumpadParenLeft = 105,
        /// <summary><c>NumpadParenRight</c> enum value.</summary>
        NumpadParenRight = 106,
        /// <summary><c>NumpadSubtract</c> enum value.</summary>
        NumpadSubtract = 107,
        /// <summary><c>NumpadSeparator</c> enum value.</summary>
        NumpadSeparator = 108,
        /// <summary><c>NumpadUp</c> enum value.</summary>
        NumpadUp = 109,
        /// <summary><c>NumpadDown</c> enum value.</summary>
        NumpadDown = 110,
        /// <summary><c>NumpadRight</c> enum value.</summary>
        NumpadRight = 111,
        /// <summary><c>NumpadLeft</c> enum value.</summary>
        NumpadLeft = 112,
        /// <summary><c>NumpadBegin</c> enum value.</summary>
        NumpadBegin = 113,
        /// <summary><c>NumpadHome</c> enum value.</summary>
        NumpadHome = 114,
        /// <summary><c>NumpadEnd</c> enum value.</summary>
        NumpadEnd = 115,
        /// <summary><c>NumpadInsert</c> enum value.</summary>
        NumpadInsert = 116,
        /// <summary><c>NumpadDelete</c> enum value.</summary>
        NumpadDelete = 117,
        /// <summary><c>NumpadPageUp</c> enum value.</summary>
        NumpadPageUp = 118,
        /// <summary><c>NumpadPageDown</c> enum value.</summary>
        NumpadPageDown = 119,
        /// <summary><c>Escape</c> enum value.</summary>
        Escape = 120,
        /// <summary><c>F1</c> enum value.</summary>
        F1 = 121,
        /// <summary><c>F2</c> enum value.</summary>
        F2 = 122,
        /// <summary><c>F3</c> enum value.</summary>
        F3 = 123,
        /// <summary><c>F4</c> enum value.</summary>
        F4 = 124,
        /// <summary><c>F5</c> enum value.</summary>
        F5 = 125,
        /// <summary><c>F6</c> enum value.</summary>
        F6 = 126,
        /// <summary><c>F7</c> enum value.</summary>
        F7 = 127,
        /// <summary><c>F8</c> enum value.</summary>
        F8 = 128,
        /// <summary><c>F9</c> enum value.</summary>
        F9 = 129,
        /// <summary><c>F10</c> enum value.</summary>
        F10 = 130,
        /// <summary><c>F11</c> enum value.</summary>
        F11 = 131,
        /// <summary><c>F12</c> enum value.</summary>
        F12 = 132,
        /// <summary><c>F13</c> enum value.</summary>
        F13 = 133,
        /// <summary><c>F14</c> enum value.</summary>
        F14 = 134,
        /// <summary><c>F15</c> enum value.</summary>
        F15 = 135,
        /// <summary><c>F16</c> enum value.</summary>
        F16 = 136,
        /// <summary><c>F17</c> enum value.</summary>
        F17 = 137,
        /// <summary><c>F18</c> enum value.</summary>
        F18 = 138,
        /// <summary><c>F19</c> enum value.</summary>
        F19 = 139,
        /// <summary><c>F20</c> enum value.</summary>
        F20 = 140,
        /// <summary><c>F21</c> enum value.</summary>
        F21 = 141,
        /// <summary><c>F22</c> enum value.</summary>
        F22 = 142,
        /// <summary><c>F23</c> enum value.</summary>
        F23 = 143,
        /// <summary><c>F24</c> enum value.</summary>
        F24 = 144,
        /// <summary><c>F25</c> enum value.</summary>
        F25 = 145,
        /// <summary>Function modifier key.</summary>
        Fn = 146,
        /// <summary>Function modifier lock key.</summary>
        FnLock = 147,
        /// <summary>Print screen key.</summary>
        PrintScreen = 148,
        /// <summary>Scroll lock key.</summary>
        ScrollLock = 149,
        /// <summary>Pause key.</summary>
        Pause = 150,
        /// <summary>Browser back key.</summary>
        BrowserBack = 151,
        /// <summary>Browser favorites key.</summary>
        BrowserFavorites = 152,
        /// <summary>Browser forward key.</summary>
        BrowserForward = 153,
        /// <summary>Browser home key.</summary>
        BrowserHome = 154,
        /// <summary>Browser refresh key.</summary>
        BrowserRefresh = 155,
        /// <summary>Browser search key.</summary>
        BrowserSearch = 156,
        /// <summary>Browser stop key.</summary>
        BrowserStop = 157,
        /// <summary>Media eject key.</summary>
        Eject = 158,
        /// <summary>Application launcher key 1.</summary>
        LaunchApp1 = 159,
        /// <summary>Application launcher key 2.</summary>
        LaunchApp2 = 160,
        /// <summary>Mail launcher key.</summary>
        LaunchMail = 161,
        /// <summary>Media play/pause key.</summary>
        MediaPlayPause = 162,
        /// <summary>Media selector key.</summary>
        MediaSelect = 163,
        /// <summary>Media stop key.</summary>
        MediaStop = 164,
        /// <summary>Next media track key.</summary>
        MediaTrackNext = 165,
        /// <summary>Previous media track key.</summary>
        MediaTrackPrevious = 166,
        /// <summary>Power key.</summary>
        Power = 167,
        /// <summary>Sleep key.</summary>
        Sleep = 168,
        /// <summary>Volume down key.</summary>
        AudioVolumeDown = 169,
        /// <summary>Mute key.</summary>
        AudioVolumeMute = 170,
        /// <summary>Volume up key.</summary>
        AudioVolumeUp = 171,
        /// <summary>Wake key.</summary>
        WakeUp = 172,
        /// <summary>Copy key.</summary>
        Copy = 173,
        /// <summary>Cut key.</summary>
        Cut = 174,
        /// <summary>Paste key.</summary>
        Paste = 175,
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
        /// <summary>The active shift modifier is on the right side.</summary>
        ShiftSide = 1 << 6,
        /// <summary>The active control modifier is on the right side.</summary>
        CtrlSide = 1 << 7,
        /// <summary>The active alt modifier is on the right side.</summary>
        AltSide = 1 << 8,
        /// <summary>The active super modifier is on the right side.</summary>
        SuperSide = 1 << 9,
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
        /// <summary>ConEmu sleep request.</summary>
        ConEmuSleep = 12,
        /// <summary>ConEmu message-box request.</summary>
        ConEmuShowMessageBox = 13,
        /// <summary>ConEmu tab-title request.</summary>
        ConEmuChangeTabTitle = 14,
        /// <summary>ConEmu progress report.</summary>
        ConEmuProgressReport = 15,
        /// <summary>ConEmu input-wait request.</summary>
        ConEmuWaitInput = 16,
        /// <summary>ConEmu GUI macro request.</summary>
        ConEmuGuiMacro = 17,
        /// <summary>ConEmu process-start request.</summary>
        ConEmuRunProcess = 18,
        /// <summary>ConEmu environment-output request.</summary>
        ConEmuOutputEnvironmentVariable = 19,
        /// <summary>ConEmu xterm-emulation request.</summary>
        ConEmuXtermEmulation = 20,
        /// <summary>ConEmu comment.</summary>
        ConEmuComment = 21,
        /// <summary>Kitty text-sizing command.</summary>
        KittyTextSizing = 22,
        /// <summary>Kitty clipboard protocol command.</summary>
        KittyClipboardProtocol = 23,
        /// <summary>Kitty drag-and-drop protocol command.</summary>
        KittyDragAndDropProtocol = 24,
        /// <summary>OSC 3008 context signal.</summary>
        /// <summary>OSC 3008 hierarchical context.</summary>
        HierarchicalContext = 25,
        /// <summary>Compatibility name for <see cref="HierarchicalContext"/>.</summary>
        ContextSignal = HierarchicalContext,
        /// <summary>Kitty desktop-notification command.</summary>
        KittyDesktopNotification = 26,
        /// <summary>Unsupported OSC with retained bytes.</summary>
        Unknown = 27,
        /// <summary>OSC 7501 report or feature query.</summary>
        ProgramStatus = 28,
    }

    /// <summary>Data selector when reading typed OSC command data.</summary>
    public enum GhosttyOscCommandData : int
    {
        /// <summary><c>Invalid</c> enum value.</summary>
        Invalid = 0,
        /// <summary><c>ChangeWindowTitleStr</c> enum value.</summary>
        ChangeWindowTitleStr = 1,
        /// <summary>Borrowed raw unsupported OSC bytes.</summary>
        UnknownContent = 2,
        /// <summary>Whether capture was truncated.</summary>
        UnknownTruncated = 3,
        /// <summary>Unsupported OSC terminator.</summary>
        UnknownTerminator = 4,
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
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventNew(nint allocator, out nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void KeyEventFree(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_action")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventSetAction(nint keyEvent, GhosttyVtKeyAction action);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_action")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyVtKeyAction KeyEventGetAction(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_key")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventSetKey(nint keyEvent, GhosttyVtKey key);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_key")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyVtKey KeyEventGetKey(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventSetMods(nint keyEvent, GhosttyVtMods mods);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyVtMods KeyEventGetMods(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_consumed_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventSetConsumedMods(nint keyEvent, GhosttyVtMods consumedMods);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_consumed_mods")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyVtMods KeyEventGetConsumedMods(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_composing")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventSetComposing(nint keyEvent, [MarshalAs(UnmanagedType.U1)] bool composing);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_composing")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool KeyEventGetComposing(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_utf8")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KeyEventSetUtf8(nint keyEvent, GhosttyString utf8);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_utf8")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyString KeyEventGetUtf8(nint keyEvent);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_set_unshifted_codepoint")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEventSetUnshiftedCodepoint(nint keyEvent, uint codepoint);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_event_get_unshifted_codepoint")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint KeyEventGetUnshiftedCodepoint(nint keyEvent);

    // ────────────────────── Key Encoder Functions ───────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void KeyEncoderFree(nint encoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KeyEncoderSetopt(nint encoder, int option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_set_from_terminal")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult KeyEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_key_encoder_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult KeyEncoderEncode(
        nint encoder, nint keyEvent,
        byte* outBuf, nuint outBufSize, out nuint outLen);

    // ────────────────────── OSC Parser Functions ────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult OscNew(nint allocator, out nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OscFree(nint parser);

    /// <summary>Configures a standalone OSC parser; options survive reset.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_osc_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult OscSet(nint parser, GhosttyOscOption option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OscReset(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void OscNext(nint parser, byte b);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_end")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint OscEnd(nint parser, byte terminator);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_command_type")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyOscCommandType OscCommandType(nint command);

    [LibraryImport(LibName, EntryPoint = "ghostty_osc_command_data")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult OscCommandData(nint command, GhosttyOscCommandData dataType, void* outPtr);

    // ────────────────────── SGR Parser Functions ────────────────────

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SgrNew(nint allocator, out nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SgrFree(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_reset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SgrReset(nint parser);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_set_params")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SgrSetParams(nint parser, ushort* param, byte* separators, nuint len);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static unsafe partial bool SgrNext(nint parser, void* attr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_unknown_full")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial nuint SgrUnknownFull(GhosttySgrUnknown unknown, ushort** ptr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_unknown_partial")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial nuint SgrUnknownPartial(GhosttySgrUnknown unknown, ushort** ptr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_attribute_tag")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttySgrAttributeTag SgrAttributeTag(in GhosttySgrAttribute attr);

    [LibraryImport(LibName, EntryPoint = "ghostty_sgr_attribute_value")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttySgrAttributeValue* SgrAttributeValue(GhosttySgrAttribute* attr);

    // ────────────────────── Paste Functions ─────────────────────────

    /// <summary>Identifies whether a paste came from a clipboard gesture or inserted text.</summary>
    public enum GhosttyPasteSource : int
    {
        /// <summary>User-initiated clipboard paste.</summary>
        Clipboard = 0,
        /// <summary>Text insertion such as IME commit or drag and drop.</summary>
        Text = 1,
    }

    /// <summary>Native terminal-aware paste request.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyPasteRequest
    {
        public nuint Size;
        public GhosttyClipboardLocation Location;
        public GhosttyPasteSource Source;
        public GhosttyString* Mimes;
        public nuint MimesLength;
        public GhosttyMimeReader Reader;

        [MarshalAs(UnmanagedType.U1)]
        public bool AllowUnsafe;

        public static GhosttyPasteRequest CreateSized()
        {
            return new GhosttyPasteRequest
            {
                Size = (nuint)Marshal.SizeOf<GhosttyPasteRequest>(),
            };
        }
    }

    /// <summary>Pastes according to terminal modes. <paramref name="written"/>
    /// is defined only when the result is <see cref="GhosttyResult.Success"/>;
    /// errors send no output to the PTY.</summary>
    [LibraryImport(LibName, EntryPoint = "ghostty_terminal_paste")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult TerminalPaste(
        nint terminal,
        GhosttyPasteRequest* paste,
        [MarshalAs(UnmanagedType.U1)] out bool written);

    [LibraryImport(LibName, EntryPoint = "ghostty_paste_is_safe")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
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
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial void ColorRgbGet(
        in GhosttyColorRgb color,
        byte* r,
        byte* g,
        byte* b);

    [LibraryImport(LibName, EntryPoint = "ghostty_type_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint TypeJson();
}
