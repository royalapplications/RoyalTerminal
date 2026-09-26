// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

internal static partial class MacOsKeyboardLayout
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal static string? Translate(KeyEventArgs key, KeyModifiers modifiers) => Translate(key, modifiers, 1, out _);

    internal static bool IsDeadKey(KeyEventArgs key)
    {
        Translate(key, key.KeyModifiers, 0, out bool dead);
        return dead;
    }

    private static unsafe string? Translate(KeyEventArgs key, KeyModifiers modifiers, uint options, out bool isDead)
    {
        isDead = false;
        ushort code = ScanCode(key.PhysicalKey);
        if (code == ushort.MaxValue) return null;
        nint source = TISCopyCurrentKeyboardLayoutInputSource();
        if (source == 0) return null;
        nint library = 0;
        try
        {
            library = NativeLibrary.Load(Carbon);
            nint property = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kTISPropertyUnicodeKeyLayoutData"));
            nint data = TISGetInputSourceProperty(source, property);
            if (data == 0) return null;
            // UCKeyTranslate uses Carbon modifiers shifted by eight. Control
            // and Command are shortcuts, not text-producing layout modifiers.
            uint flags = ((modifiers & KeyModifiers.Shift) != 0 ? 2U : 0) |
                ((modifiers & KeyModifiers.Alt) != 0 ? 8U : 0);
            uint dead = 0;
            nuint count = 0;
            Span<char> buffer = stackalloc char[8];
            fixed (char* output = buffer)
            {
                int status = UCKeyTranslate(CFDataGetBytePtr(data), code, 0, flags,
                    LMGetKbdType(), options, &dead, (nuint)buffer.Length, &count, output);
                isDead = status == 0 && dead != 0;
                return status == 0 && count > 0 && count <= (nuint)buffer.Length ? new string(buffer[..(int)count]) : null;
            }
        }
        finally
        {
            CFRelease(source);
            if (library != 0) NativeLibrary.Free(library);
        }
    }

    // Hardware positions, not character guesses. Characters come exclusively
    // from the current TIS layout (including the layout underneath an IME).
    internal static ushort ScanCode(PhysicalKey key) => key switch
    {
        PhysicalKey.A => 0x00, PhysicalKey.S => 0x01, PhysicalKey.D => 0x02, PhysicalKey.F => 0x03,
        PhysicalKey.H => 0x04, PhysicalKey.G => 0x05, PhysicalKey.Z => 0x06, PhysicalKey.X => 0x07,
        PhysicalKey.C => 0x08, PhysicalKey.V => 0x09, PhysicalKey.B => 0x0B, PhysicalKey.Q => 0x0C,
        PhysicalKey.W => 0x0D, PhysicalKey.E => 0x0E, PhysicalKey.R => 0x0F, PhysicalKey.Y => 0x10,
        PhysicalKey.T => 0x11, PhysicalKey.Digit1 => 0x12, PhysicalKey.Digit2 => 0x13, PhysicalKey.Digit3 => 0x14,
        PhysicalKey.Digit4 => 0x15, PhysicalKey.Digit6 => 0x16, PhysicalKey.Digit5 => 0x17, PhysicalKey.Equal => 0x18,
        PhysicalKey.Digit9 => 0x19, PhysicalKey.Digit7 => 0x1A, PhysicalKey.Minus => 0x1B, PhysicalKey.Digit8 => 0x1C,
        PhysicalKey.Digit0 => 0x1D, PhysicalKey.BracketRight => 0x1E, PhysicalKey.O => 0x1F, PhysicalKey.U => 0x20,
        PhysicalKey.BracketLeft => 0x21, PhysicalKey.I => 0x22, PhysicalKey.P => 0x23, PhysicalKey.L => 0x25,
        PhysicalKey.J => 0x26, PhysicalKey.Quote => 0x27, PhysicalKey.K => 0x28, PhysicalKey.Semicolon => 0x29,
        PhysicalKey.Backslash => 0x2A, PhysicalKey.Comma => 0x2B, PhysicalKey.Slash => 0x2C, PhysicalKey.N => 0x2D,
        PhysicalKey.M => 0x2E, PhysicalKey.Period => 0x2F, PhysicalKey.Space => 0x31, PhysicalKey.Backquote => 0x32,
        PhysicalKey.IntlBackslash => 0x0A, PhysicalKey.IntlYen => 0x5D, PhysicalKey.IntlRo => 0x5E,
        PhysicalKey.NumPad0 => 0x52, PhysicalKey.NumPad1 => 0x53, PhysicalKey.NumPad2 => 0x54,
        PhysicalKey.NumPad3 => 0x55, PhysicalKey.NumPad4 => 0x56, PhysicalKey.NumPad5 => 0x57,
        PhysicalKey.NumPad6 => 0x58, PhysicalKey.NumPad7 => 0x59, PhysicalKey.NumPad8 => 0x5B,
        PhysicalKey.NumPad9 => 0x5C, PhysicalKey.NumPadAdd => 0x45, PhysicalKey.NumPadComma => 0x5F,
        PhysicalKey.NumPadDecimal => 0x41, PhysicalKey.NumPadDivide => 0x4B, PhysicalKey.NumPadEqual => 0x51,
        PhysicalKey.NumPadMultiply => 0x43, PhysicalKey.NumPadSubtract => 0x4E,
        _ => ushort.MaxValue,
    };

    [LibraryImport(Carbon)] private static partial nint TISCopyCurrentKeyboardLayoutInputSource();
    [LibraryImport(Carbon)] private static partial nint TISGetInputSourceProperty(nint source, nint property);
    [LibraryImport(Carbon)] private static partial byte LMGetKbdType();
    [LibraryImport(Carbon)] private static unsafe partial int UCKeyTranslate(nint layout, ushort code, ushort action,
        uint modifiers, uint keyboardType, uint options, uint* dead, nuint capacity, nuint* length, char* output);
    [LibraryImport(CoreFoundation)] private static partial nint CFDataGetBytePtr(nint data);
    [LibraryImport(CoreFoundation)] private static partial void CFRelease(nint value);
}
