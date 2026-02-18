// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — VT key sequence encoder for transport fallback input.

using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Encodes Avalonia key events into VT escape sequences for transport/PTY fallback input paths.
/// </summary>
internal static class TerminalKeySequenceEncoder
{
    private const string Escape = "\x1B";

    public static bool TryEncode(
        Key key,
        KeyModifiers modifiers,
        in TerminalModeState modeState,
        out string sequence)
    {
        bool meta = modifiers.HasFlag(KeyModifiers.Meta);
        if (meta)
        {
            sequence = string.Empty;
            return false;
        }

        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
        bool alt = modifiers.HasFlag(KeyModifiers.Alt);

        if (IsModifierKey(key))
        {
            sequence = string.Empty;
            return false;
        }

        if (ctrl && TryEncodeControlChord(key, out string controlSequence))
        {
            sequence = PrefixWithEscape(controlSequence, alt);
            return true;
        }

        if (TryEncodeNavigationOrEditingKey(key, shift, alt, ctrl, modeState.ApplicationCursorKeys, out sequence))
        {
            return true;
        }

        if (TryEncodeFunctionKey(key, shift, alt, ctrl, out sequence))
        {
            return true;
        }

        if (TryEncodeKeypadKey(key, shift, alt, ctrl, modeState.ApplicationKeypad, out sequence))
        {
            return true;
        }

        if (alt && !ctrl && !shift && key == Key.Space)
        {
            sequence = $"{Escape} ";
            return true;
        }

        sequence = string.Empty;
        return false;
    }

    private static bool TryEncodeNavigationOrEditingKey(
        Key key,
        bool shift,
        bool alt,
        bool ctrl,
        bool applicationCursorKeys,
        out string sequence)
    {
        sequence = string.Empty;
        bool hasModifiers = shift || alt || ctrl;
        int modifierParameter = GetModifierParameter(shift, alt, ctrl);

        if (TryGetArrowOrHomeEndFinal(key, out char final))
        {
            if (!hasModifiers)
            {
                if (applicationCursorKeys)
                {
                    sequence = $"{Escape}O{final}";
                    return true;
                }

                sequence = $"{Escape}[{final}";
                return true;
            }

            sequence = $"{Escape}[1;{modifierParameter}{final}";
            return true;
        }

        if (TryGetTildeCode(key, out int tildeCode))
        {
            if (!hasModifiers)
            {
                sequence = $"{Escape}[{tildeCode}~";
                return true;
            }

            sequence = $"{Escape}[{tildeCode};{modifierParameter}~";
            return true;
        }

        switch (key)
        {
            case Key.Return:
                sequence = PrefixWithEscape("\r", alt);
                return true;

            case Key.Back:
                // ctrl+backspace maps to BS; default backspace maps to DEL.
                sequence = PrefixWithEscape(ctrl ? "\b" : "\x7F", alt);
                return true;

            case Key.Escape:
                sequence = PrefixWithEscape(Escape, alt);
                return true;

            case Key.Tab:
                if (shift)
                {
                    sequence = (alt || ctrl)
                        ? $"{Escape}[1;{modifierParameter}Z"
                        : $"{Escape}[Z";
                    return true;
                }

                if (ctrl)
                {
                    return false;
                }

                sequence = PrefixWithEscape("\t", alt);
                return true;

            default:
                return false;
        }
    }

    private static bool TryEncodeFunctionKey(
        Key key,
        bool shift,
        bool alt,
        bool ctrl,
        out string sequence)
    {
        sequence = string.Empty;
        bool hasModifiers = shift || alt || ctrl;
        int modifierParameter = GetModifierParameter(shift, alt, ctrl);

        if (TryGetSs3FunctionFinal(key, out char ss3Final))
        {
            if (!hasModifiers)
            {
                sequence = $"{Escape}O{ss3Final}";
                return true;
            }

            sequence = $"{Escape}[1;{modifierParameter}{ss3Final}";
            return true;
        }

        if (!TryGetTildeFunctionCode(key, out int tildeCode))
        {
            return false;
        }

        if (!hasModifiers)
        {
            sequence = $"{Escape}[{tildeCode}~";
            return true;
        }

        sequence = $"{Escape}[{tildeCode};{modifierParameter}~";
        return true;
    }

    private static bool TryEncodeKeypadKey(
        Key key,
        bool shift,
        bool alt,
        bool ctrl,
        bool applicationKeypad,
        out string sequence)
    {
        sequence = string.Empty;

        if (shift || ctrl)
        {
            return false;
        }

        if (applicationKeypad)
        {
            if (!TryGetApplicationKeypadSequence(key, out string applicationSequence))
            {
                return false;
            }

            sequence = PrefixWithEscape(applicationSequence, alt);
            return true;
        }

        if (!TryGetNormalKeypadSequence(key, out string normalSequence))
        {
            return false;
        }

        sequence = PrefixWithEscape(normalSequence, alt);
        return true;
    }

    private static bool TryEncodeControlChord(Key key, out string sequence)
    {
        sequence = string.Empty;

        if (key >= Key.A && key <= Key.Z)
        {
            int offset = key - Key.A + 1;
            sequence = new string((char)offset, 1);
            return true;
        }

        switch (key)
        {
            case Key.Space:
            case Key.D2:
                sequence = "\0";
                return true;
            case Key.D3:
            case Key.OemOpenBrackets:
                sequence = Escape;
                return true;
            case Key.D4:
            case Key.OemBackslash:
                sequence = "\x1C";
                return true;
            case Key.D5:
            case Key.OemCloseBrackets:
                sequence = "\x1D";
                return true;
            case Key.D6:
                sequence = "\x1E";
                return true;
            case Key.D7:
            case Key.OemMinus:
            case Key.Oem2:
                sequence = "\x1F";
                return true;
            case Key.D8:
                sequence = "\x7F";
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetArrowOrHomeEndFinal(Key key, out char final)
    {
        switch (key)
        {
            case Key.Up:
                final = 'A';
                return true;
            case Key.Down:
                final = 'B';
                return true;
            case Key.Right:
                final = 'C';
                return true;
            case Key.Left:
                final = 'D';
                return true;
            case Key.Home:
                final = 'H';
                return true;
            case Key.End:
                final = 'F';
                return true;
            default:
                final = default;
                return false;
        }
    }

    private static bool TryGetTildeCode(Key key, out int code)
    {
        switch (key)
        {
            case Key.Insert:
                code = 2;
                return true;
            case Key.Delete:
                code = 3;
                return true;
            case Key.PageUp:
                code = 5;
                return true;
            case Key.PageDown:
                code = 6;
                return true;
            default:
                code = default;
                return false;
        }
    }

    private static bool TryGetSs3FunctionFinal(Key key, out char final)
    {
        switch (key)
        {
            case Key.F1:
                final = 'P';
                return true;
            case Key.F2:
                final = 'Q';
                return true;
            case Key.F3:
                final = 'R';
                return true;
            case Key.F4:
                final = 'S';
                return true;
            default:
                final = default;
                return false;
        }
    }

    private static bool TryGetTildeFunctionCode(Key key, out int code)
    {
        switch (key)
        {
            case Key.F5:
                code = 15;
                return true;
            case Key.F6:
                code = 17;
                return true;
            case Key.F7:
                code = 18;
                return true;
            case Key.F8:
                code = 19;
                return true;
            case Key.F9:
                code = 20;
                return true;
            case Key.F10:
                code = 21;
                return true;
            case Key.F11:
                code = 23;
                return true;
            case Key.F12:
                code = 24;
                return true;
            case Key.F13:
                code = 25;
                return true;
            case Key.F14:
                code = 26;
                return true;
            case Key.F15:
                code = 28;
                return true;
            case Key.F16:
                code = 29;
                return true;
            case Key.F17:
                code = 31;
                return true;
            case Key.F18:
                code = 32;
                return true;
            case Key.F19:
                code = 33;
                return true;
            case Key.F20:
                code = 34;
                return true;
            default:
                code = default;
                return false;
        }
    }

    private static bool TryGetApplicationKeypadSequence(Key key, out string sequence)
    {
        switch (key)
        {
            case Key.NumPad0:
                sequence = $"{Escape}Op";
                return true;
            case Key.NumPad1:
                sequence = $"{Escape}Oq";
                return true;
            case Key.NumPad2:
                sequence = $"{Escape}Or";
                return true;
            case Key.NumPad3:
                sequence = $"{Escape}Os";
                return true;
            case Key.NumPad4:
                sequence = $"{Escape}Ot";
                return true;
            case Key.NumPad5:
                sequence = $"{Escape}Ou";
                return true;
            case Key.NumPad6:
                sequence = $"{Escape}Ov";
                return true;
            case Key.NumPad7:
                sequence = $"{Escape}Ow";
                return true;
            case Key.NumPad8:
                sequence = $"{Escape}Ox";
                return true;
            case Key.NumPad9:
                sequence = $"{Escape}Oy";
                return true;
            case Key.Decimal:
                sequence = $"{Escape}On";
                return true;
            case Key.Divide:
                sequence = $"{Escape}Oo";
                return true;
            case Key.Multiply:
                sequence = $"{Escape}Oj";
                return true;
            case Key.Subtract:
                sequence = $"{Escape}Om";
                return true;
            case Key.Add:
                sequence = $"{Escape}Ok";
                return true;
            default:
                sequence = string.Empty;
                return false;
        }
    }

    private static bool TryGetNormalKeypadSequence(Key key, out string sequence)
    {
        switch (key)
        {
            case Key.NumPad0:
                sequence = "0";
                return true;
            case Key.NumPad1:
                sequence = "1";
                return true;
            case Key.NumPad2:
                sequence = "2";
                return true;
            case Key.NumPad3:
                sequence = "3";
                return true;
            case Key.NumPad4:
                sequence = "4";
                return true;
            case Key.NumPad5:
                sequence = "5";
                return true;
            case Key.NumPad6:
                sequence = "6";
                return true;
            case Key.NumPad7:
                sequence = "7";
                return true;
            case Key.NumPad8:
                sequence = "8";
                return true;
            case Key.NumPad9:
                sequence = "9";
                return true;
            case Key.Decimal:
                sequence = ".";
                return true;
            case Key.Divide:
                sequence = "/";
                return true;
            case Key.Multiply:
                sequence = "*";
                return true;
            case Key.Subtract:
                sequence = "-";
                return true;
            case Key.Add:
                sequence = "+";
                return true;
            default:
                sequence = string.Empty;
                return false;
        }
    }

    private static string PrefixWithEscape(string sequence, bool alt)
    {
        return alt ? $"{Escape}{sequence}" : sequence;
    }

    private static int GetModifierParameter(bool shift, bool alt, bool ctrl)
    {
        int parameter = 1;
        if (shift) parameter += 1;
        if (alt) parameter += 2;
        if (ctrl) parameter += 4;
        return parameter;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftShift
            or Key.RightShift
            or Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LWin
            or Key.RWin;
    }
}
