// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Windows keyboard layout text-input probing.

using System.Runtime.InteropServices;
using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

internal static partial class WindowsKeyboardLayoutTextInputProbeKeyMap
{
    public static ushort GetVirtualKey(Key key)
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
            Key.Space => 0x20,
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
            Key.Oem8 => 0xDF,
            _ => 0,
        };
    }
}

internal sealed partial class WindowsKeyboardLayoutTextInputProbe : IWindowsKeyboardLayoutTextInputProbe
{
    private const int KeyboardStateLength = 256;
    private const byte KeyPressed = 0x80;
    private const uint MapVkToVsc = 0;
    private const uint ToUnicodeExDoNotChangeKeyboardState = 0x04;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkRMenu = 0xA5;

    public bool MayProduceText(KeyEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        ushort virtualKey = WindowsKeyboardLayoutTextInputProbeKeyMap.GetVirtualKey(e.Key);
        if (virtualKey == 0)
        {
            return false;
        }

        nint keyboardLayout = GetKeyboardLayout(0);
        if (keyboardLayout == 0)
        {
            return false;
        }

        uint scanCode = MapVirtualKeyEx(virtualKey, MapVkToVsc, keyboardLayout);
        Span<byte> keyState = stackalloc byte[KeyboardStateLength];
        ApplyModifierState(keyState, e.KeyModifiers);

        return TranslateKey(virtualKey, scanCode, keyState, keyboardLayout);
    }

    private static unsafe bool TranslateKey(
        ushort virtualKey,
        uint scanCode,
        ReadOnlySpan<byte> keyState,
        nint keyboardLayout)
    {
        Span<char> buffer = stackalloc char[8];
        fixed (byte* keyStatePointer = keyState)
        fixed (char* bufferPointer = buffer)
        {
            int result = ToUnicodeEx(
                virtualKey,
                scanCode,
                keyStatePointer,
                bufferPointer,
                buffer.Length,
                ToUnicodeExDoNotChangeKeyboardState,
                keyboardLayout);

            return result != 0;
        }
    }

    private static void ApplyModifierState(Span<byte> keyState, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            keyState[VkShift] = KeyPressed;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            keyState[VkControl] = KeyPressed;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            keyState[VkMenu] = KeyPressed;
            keyState[VkRMenu] = KeyPressed;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

    [LibraryImport("user32.dll", EntryPoint = "ToUnicodeEx")]
    private static unsafe partial int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte* lpKeyState,
        char* pwszBuff,
        int cchBuff,
        uint wFlags,
        nint dwhkl);
}
