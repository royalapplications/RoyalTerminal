// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Win32 input mode sequence encoder (DECSET 9001).

using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Encodes key events as win32-input-mode records:
/// CSI Vk;Sc;Uc;Kd;Cs;Rc _
/// </summary>
internal static partial class TerminalWin32InputSequenceEncoder
{
    private const string Escape = "\x1B";
    private const uint MapVkToVscEx = 4; // MAPVK_VK_TO_VSC_EX

    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;
    private const uint EnhancedKey = 0x0100;

    public static bool TryEncode(
        Key key,
        KeyModifiers modifiers,
        string? keySymbol,
        bool keyDown,
        out string sequence)
    {
        if (!OperatingSystem.IsWindows())
        {
            sequence = string.Empty;
            return false;
        }

        ushort virtualKey = GetVirtualKey(key);
        ushort scanCode = virtualKey == 0 ? (ushort)0 : GetScanCode(virtualKey);
        int unicodeChar = keyDown ? ResolveUnicodeChar(key, modifiers, keySymbol) : 0;
        uint controlState = GetControlKeyState(key, modifiers, virtualKey);

        sequence = $"{Escape}[{virtualKey};{scanCode};{unicodeChar};{(keyDown ? 1 : 0)};{controlState};1_";
        return true;
    }

    private static ushort GetVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return (ushort)(0x41 + (key - Key.A));
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return (ushort)(0x30 + (key - Key.D0));
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return (ushort)(0x60 + (key - Key.NumPad0));
        }

        return key switch
        {
            Key.Return => 0x0D,
            Key.Escape => 0x1B,
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Space => 0x20,
            Key.Pause => 0x13,
            Key.CapsLock => 0x14,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Up => 0x26,
            Key.Down => 0x28,
            Key.Left => 0x25,
            Key.Right => 0x27,
            Key.F1 => 0x70,
            Key.F2 => 0x71,
            Key.F3 => 0x72,
            Key.F4 => 0x73,
            Key.F5 => 0x74,
            Key.F6 => 0x75,
            Key.F7 => 0x76,
            Key.F8 => 0x77,
            Key.F9 => 0x78,
            Key.F10 => 0x79,
            Key.F11 => 0x7A,
            Key.F12 => 0x7B,
            Key.F13 => 0x7C,
            Key.F14 => 0x7D,
            Key.F15 => 0x7E,
            Key.F16 => 0x7F,
            Key.F17 => 0x80,
            Key.F18 => 0x81,
            Key.F19 => 0x82,
            Key.F20 => 0x83,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.Multiply => 0x6A,
            Key.Subtract => 0x6D,
            Key.Add => 0x6B,
            Key.OemMinus => 0xBD,
            Key.OemPlus => 0xBB,
            Key.OemOpenBrackets => 0xDB,
            Key.OemCloseBrackets => 0xDD,
            Key.OemBackslash => 0xDC,
            Key.OemPipe => 0xDC,
            Key.OemSemicolon => 0xBA,
            Key.OemQuotes => 0xDE,
            Key.OemTilde => 0xC0,
            Key.OemComma => 0xBC,
            Key.OemPeriod => 0xBE,
            Key.Oem2 => 0xBF,
            Key.LeftShift => 0xA0,
            Key.RightShift => 0xA1,
            Key.LeftCtrl => 0xA2,
            Key.RightCtrl => 0xA3,
            Key.LeftAlt => 0xA4,
            Key.RightAlt => 0xA5,
            Key.LWin => 0x5B,
            Key.RWin => 0x5C,
            _ => 0,
        };
    }

    private static ushort GetScanCode(ushort virtualKey)
    {
        uint mapped = MapVirtualKey(virtualKey, MapVkToVscEx);
        return (ushort)(mapped & 0xFFFF);
    }

    private static int ResolveUnicodeChar(Key key, KeyModifiers modifiers, string? keySymbol)
    {
        if (!string.IsNullOrEmpty(keySymbol) &&
            Rune.TryGetRuneAt(keySymbol, 0, out Rune symbolRune))
        {
            return symbolRune.Value;
        }

        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
        bool alt = modifiers.HasFlag(KeyModifiers.Alt);
        if (ctrl && !alt && TryGetControlCharacter(key, out int controlChar))
        {
            return controlChar;
        }

        if (TryGetSpecialUnicodeChar(key, out int special))
        {
            return special;
        }

        if (TryGetPrintableFallbackChar(key, modifiers.HasFlag(KeyModifiers.Shift), out int printable))
        {
            return printable;
        }

        return 0;
    }

    private static bool TryGetControlCharacter(Key key, out int unicodeChar)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            unicodeChar = key - Key.A + 1;
            return true;
        }

        switch (key)
        {
            case Key.Space:
            case Key.D2:
                unicodeChar = 0;
                return true;
            case Key.D3:
            case Key.OemOpenBrackets:
                unicodeChar = 0x1B;
                return true;
            case Key.D4:
            case Key.OemBackslash:
                unicodeChar = 0x1C;
                return true;
            case Key.D5:
            case Key.OemCloseBrackets:
                unicodeChar = 0x1D;
                return true;
            case Key.D6:
                unicodeChar = 0x1E;
                return true;
            case Key.D7:
            case Key.OemMinus:
            case Key.Oem2:
                unicodeChar = 0x1F;
                return true;
            case Key.D8:
                unicodeChar = 0x7F;
                return true;
            default:
                unicodeChar = 0;
                return false;
        }
    }

    private static bool TryGetSpecialUnicodeChar(Key key, out int unicodeChar)
    {
        switch (key)
        {
            case Key.Return:
                unicodeChar = '\r';
                return true;
            case Key.Tab:
                unicodeChar = '\t';
                return true;
            case Key.Back:
                unicodeChar = '\b';
                return true;
            case Key.Escape:
                unicodeChar = 0x1B;
                return true;
            case Key.Space:
                unicodeChar = ' ';
                return true;
            default:
                unicodeChar = 0;
                return false;
        }
    }

    private static bool TryGetPrintableFallbackChar(Key key, bool shift, out int unicodeChar)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            unicodeChar = (shift ? 'A' : 'a') + (key - Key.A);
            return true;
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            unicodeChar = '0' + (key - Key.D0);
            return true;
        }

        unicodeChar = key switch
        {
            Key.OemMinus => shift ? '_' : '-',
            Key.OemPlus => shift ? '+' : '=',
            Key.OemOpenBrackets => shift ? '{' : '[',
            Key.OemCloseBrackets => shift ? '}' : ']',
            Key.OemBackslash => shift ? '|' : '\\',
            Key.OemPipe => shift ? '|' : '\\',
            Key.OemSemicolon => shift ? ':' : ';',
            Key.OemQuotes => shift ? '"' : '\'',
            Key.OemTilde => shift ? '~' : '`',
            Key.OemComma => shift ? '<' : ',',
            Key.OemPeriod => shift ? '>' : '.',
            Key.Oem2 => shift ? '?' : '/',
            Key.Add => '+',
            Key.Subtract => '-',
            Key.Multiply => '*',
            Key.Divide => '/',
            Key.Decimal => '.',
            Key.NumPad0 => '0',
            Key.NumPad1 => '1',
            Key.NumPad2 => '2',
            Key.NumPad3 => '3',
            Key.NumPad4 => '4',
            Key.NumPad5 => '5',
            Key.NumPad6 => '6',
            Key.NumPad7 => '7',
            Key.NumPad8 => '8',
            Key.NumPad9 => '9',
            _ => 0,
        };

        return unicodeChar != 0;
    }

    private static uint GetControlKeyState(Key key, KeyModifiers modifiers, ushort virtualKey)
    {
        uint state = 0;

        if (modifiers.HasFlag(KeyModifiers.Shift) || key is Key.LeftShift or Key.RightShift)
        {
            state |= ShiftPressed;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            state |= key == Key.RightCtrl ? RightCtrlPressed : LeftCtrlPressed;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            state |= key == Key.RightAlt ? RightAltPressed : LeftAltPressed;
        }

        if (key == Key.LeftCtrl)
        {
            state |= LeftCtrlPressed;
        }
        else if (key == Key.RightCtrl)
        {
            state |= RightCtrlPressed;
        }

        if (key == Key.LeftAlt)
        {
            state |= LeftAltPressed;
        }
        else if (key == Key.RightAlt)
        {
            state |= RightAltPressed;
        }

        if (IsEnhancedVirtualKey(virtualKey))
        {
            state |= EnhancedKey;
        }

        return state;
    }

    private static bool IsEnhancedVirtualKey(ushort virtualKey)
    {
        return virtualKey is
            0x2D or // VK_INSERT
            0x2E or // VK_DELETE
            0x24 or // VK_HOME
            0x23 or // VK_END
            0x21 or // VK_PRIOR
            0x22 or // VK_NEXT
            0x25 or // VK_LEFT
            0x26 or // VK_UP
            0x27 or // VK_RIGHT
            0x28 or // VK_DOWN
            0x6F or // VK_DIVIDE
            0xA3 or // VK_RCONTROL
            0xA5;   // VK_RMENU
    }

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint uCode, uint uMapType);
}
