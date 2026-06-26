// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Windows keyboard input normalization.

using System.Collections.Generic;
using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

internal sealed class WindowsTerminalKeyboardInputNormalizer : DefaultTerminalKeyboardInputNormalizer
{
    private readonly HashSet<Key> _suppressedAltGrTextKeys = [];
    private readonly IWindowsKeyboardLayoutTextInputProbe _textInputProbe;
    private bool _rightAltDown;

    public WindowsTerminalKeyboardInputNormalizer()
        : this(new WindowsKeyboardLayoutTextInputProbe())
    {
    }

    internal WindowsTerminalKeyboardInputNormalizer(IWindowsKeyboardLayoutTextInputProbe textInputProbe)
    {
        _textInputProbe = textInputProbe ?? throw new ArgumentNullException(nameof(textInputProbe));
    }

    public override TerminalKeyboardInputAction HandleKeyDown(KeyEventArgs e, in TerminalModeState modeState)
    {
        TrackRightAltState(e.Key, isKeyDown: true);

        if (ShouldSuppressAltGrTextKey(e, modeState))
        {
            _suppressedAltGrTextKeys.Add(e.Key);
            return TerminalKeyboardInputAction.SuppressForTextInput;
        }

        return TerminalKeyboardInputAction.Forward;
    }

    public override TerminalKeyboardInputAction HandleKeyUp(KeyEventArgs e, in TerminalModeState modeState)
    {
        TrackRightAltState(e.Key, isKeyDown: false);

        if (!IsIsolatedModifierKey(e.Key) && _suppressedAltGrTextKeys.Remove(e.Key))
        {
            return TerminalKeyboardInputAction.SuppressForTextInput;
        }

        return TerminalKeyboardInputAction.Forward;
    }

    private bool ShouldSuppressAltGrTextKey(KeyEventArgs e, in TerminalModeState modeState)
    {
        if (modeState.Win32InputMode ||
            IsIsolatedModifierKey(e.Key) ||
            !e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        return (_rightAltDown && IsPotentialTextInputKey(e.Key)) ||
               _textInputProbe.MayProduceText(e) ||
               IsLikelyAltGrTextInputKey(e);
    }

    private static bool IsLikelyAltGrTextInputKey(KeyEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.KeySymbol) && IsNonControlTextSymbol(e.KeySymbol))
        {
            return true;
        }

        return IsDigitOrOemTextInputKey(e.Key) ||
               e.Key == Key.E;
    }

    private static bool IsNonControlTextSymbol(string keySymbol)
    {
        return keySymbol.Length > 1 ||
               IsTextSymbolCharacter(keySymbol[0]);
    }

    private static bool IsTextSymbolCharacter(char value)
    {
        return value >= 0x80 ||
               char.IsPunctuation(value) ||
               char.IsSymbol(value) ||
               char.IsSeparator(value);
    }

    private static bool IsDigitOrOemTextInputKey(Key key)
    {
        return key is >= Key.D0 and <= Key.D9 ||
               key is Key.Space
                   or Key.OemMinus
                   or Key.OemPlus
                   or Key.OemOpenBrackets
                   or Key.OemCloseBrackets
                   or Key.OemBackslash
                   or Key.OemPipe
                   or Key.OemSemicolon
                   or Key.OemQuotes
                   or Key.OemTilde
                   or Key.OemComma
                   or Key.OemPeriod
                   or Key.Oem2
                   or Key.Oem8;
    }

    private static bool IsPotentialTextInputKey(Key key)
    {
        return key is >= Key.A and <= Key.Z ||
               IsDigitOrOemTextInputKey(key);
    }

    private static bool IsIsolatedModifierKey(Key key)
    {
        return key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }

    private void TrackRightAltState(Key key, bool isKeyDown)
    {
        if (key == Key.RightAlt)
        {
            _rightAltDown = isKeyDown;
        }
    }
}
