// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

// Console records distinguish Ctrl/Alt sides through flags and Shift sides
// through scan codes. Keep each pressed key so releasing one side preserves the other.
internal struct TerminalWin32ModifierState
{
    private uint _keys;

    public void Update(Key key, bool keyDown)
    {
        uint bit = GetBit(key);
        _keys = keyDown ? _keys | bit : _keys & ~bit;
    }

    public readonly uint GetControlState(Key key, KeyModifiers reported)
    {
        // Avalonia can report the pre-transition aggregate state on modifier events.
        reported &= ~GetModifier(key);
        uint state = 0;
        if ((_keys & 3) != 0 || (reported & KeyModifiers.Shift) != 0) state |= 16;
        if ((_keys & 4) != 0) state |= 8;
        if ((_keys & 8) != 0) state |= 4;
        if ((_keys & 12) == 0 && (reported & KeyModifiers.Control) != 0) state |= 8;
        if ((_keys & 16) != 0) state |= 2;
        if ((_keys & 32) != 0) state |= 1;
        if ((_keys & 48) == 0 && (reported & KeyModifiers.Alt) != 0) state |= 2;
        return state;
    }

    public static bool IsModifier(Key key) => GetBit(key) != 0;

    private static uint GetBit(Key key) => key switch
    {
        Key.LeftShift => 1,
        Key.RightShift => 2,
        Key.LeftCtrl => 4,
        Key.RightCtrl => 8,
        Key.LeftAlt => 16,
        Key.RightAlt => 32,
        Key.LWin => 64,
        Key.RWin => 128,
        _ => 0,
    };

    private static KeyModifiers GetModifier(Key key) => key switch
    {
        Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
        Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
        Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
        Key.LWin or Key.RWin => KeyModifiers.Meta,
        _ => KeyModifiers.None,
    };
}
