// Licensed under the MIT License.
// RoyalTerminal.Avalonia - macOS native keycode mapping for Ghostty input.

using System.Runtime.Versioning;
using Avalonia.Input;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Input;

/// <summary>
/// Maps Avalonia keys to macOS native virtual keycodes as expected by
/// Ghostty's ghostty_input_key_s.keycode field.
///
/// The keycode field is looked up in Ghostty's keycodes table (column index 4 = Mac)
/// to resolve the physical key identity. If the lookup fails, the key is treated
/// as unidentified and no text will be encoded from it.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeyMapping
{
    // Sentinel indicating no mapping exists.
    public const uint Unmapped = 0xFFFF;

    /// <summary>
    /// Converts an Avalonia Key to a macOS native virtual keycode.
    /// Returns <see cref="Unmapped"/> if no mapping exists.
    /// </summary>
    public static uint ConvertKeyToMacKeycode(Key key) => key switch
    {
        // Letters — macOS ANSI keycodes (NOT alphabetical!)
        Key.A => 0x00,
        Key.S => 0x01,
        Key.D => 0x02,
        Key.F => 0x03,
        Key.H => 0x04,
        Key.G => 0x05,
        Key.Z => 0x06,
        Key.X => 0x07,
        Key.C => 0x08,
        Key.V => 0x09,
        Key.B => 0x0B,
        Key.Q => 0x0C,
        Key.W => 0x0D,
        Key.E => 0x0E,
        Key.R => 0x0F,
        Key.Y => 0x10,
        Key.T => 0x11,
        Key.O => 0x1F,
        Key.U => 0x20,
        Key.I => 0x22,
        Key.P => 0x23,
        Key.L => 0x25,
        Key.J => 0x26,
        Key.K => 0x28,
        Key.N => 0x2D,
        Key.M => 0x2E,

        // Digits
        Key.D1 => 0x12,
        Key.D2 => 0x13,
        Key.D3 => 0x14,
        Key.D4 => 0x15,
        Key.D5 => 0x17,
        Key.D6 => 0x16,
        Key.D7 => 0x1A,
        Key.D8 => 0x1C,
        Key.D9 => 0x19,
        Key.D0 => 0x1D,

        // Special characters
        Key.Return => 0x24,       // kVK_Return
        Key.Escape => 0x35,       // kVK_Escape
        Key.Back => 0x33,         // kVK_Delete (Backspace)
        Key.Tab => 0x30,          // kVK_Tab
        Key.Space => 0x31,        // kVK_Space
        Key.OemMinus => 0x1B,     // kVK_ANSI_Minus
        Key.OemPlus => 0x18,      // kVK_ANSI_Equal
        Key.OemOpenBrackets => 0x21,  // kVK_ANSI_LeftBracket
        Key.OemCloseBrackets => 0x1E, // kVK_ANSI_RightBracket
        Key.OemBackslash => 0x2A, // kVK_ANSI_Backslash
        Key.OemPipe => 0x2A,      // same as backslash
        Key.OemSemicolon => 0x29, // kVK_ANSI_Semicolon
        Key.OemQuotes => 0x27,    // kVK_ANSI_Quote
        Key.OemTilde => 0x32,     // kVK_ANSI_Grave
        Key.OemComma => 0x2B,     // kVK_ANSI_Comma
        Key.OemPeriod => 0x2F,    // kVK_ANSI_Period
        Key.Oem2 => 0x2C,         // kVK_ANSI_Slash

        // Function keys
        Key.F1 => 0x7A,
        Key.F2 => 0x78,
        Key.F3 => 0x63,
        Key.F4 => 0x76,
        Key.F5 => 0x60,
        Key.F6 => 0x61,
        Key.F7 => 0x62,
        Key.F8 => 0x64,
        Key.F9 => 0x65,
        Key.F10 => 0x6D,
        Key.F11 => 0x67,
        Key.F12 => 0x6F,

        // Navigation
        Key.Insert => 0x72,       // kVK_Help
        Key.Home => 0x73,         // kVK_Home
        Key.PageUp => 0x74,       // kVK_PageUp
        Key.Delete => 0x75,       // kVK_ForwardDelete
        Key.End => 0x77,          // kVK_End
        Key.PageDown => 0x79,     // kVK_PageDown

        // Arrow keys
        Key.Right => 0x7C,        // kVK_RightArrow
        Key.Left => 0x7B,         // kVK_LeftArrow
        Key.Down => 0x7D,         // kVK_DownArrow
        Key.Up => 0x7E,           // kVK_UpArrow

        // Numpad
        Key.NumPad0 => 0x52,
        Key.NumPad1 => 0x53,
        Key.NumPad2 => 0x54,
        Key.NumPad3 => 0x55,
        Key.NumPad4 => 0x56,
        Key.NumPad5 => 0x57,
        Key.NumPad6 => 0x58,
        Key.NumPad7 => 0x59,
        Key.NumPad8 => 0x5B,
        Key.NumPad9 => 0x5C,
        Key.Decimal => 0x41,      // kVK_ANSI_KeypadDecimal
        Key.Divide => 0x4B,       // kVK_ANSI_KeypadDivide
        Key.Multiply => 0x43,     // kVK_ANSI_KeypadMultiply
        Key.Subtract => 0x4E,     // kVK_ANSI_KeypadMinus
        Key.Add => 0x45,          // kVK_ANSI_KeypadPlus

        // Modifier keys
        Key.LeftShift => 0x38,    // kVK_Shift
        Key.RightShift => 0x3C,   // kVK_RightShift
        Key.LeftCtrl => 0x3B,     // kVK_Control
        Key.RightCtrl => 0x3E,    // kVK_RightControl
        Key.LeftAlt => 0x3A,      // kVK_Option
        Key.RightAlt => 0x3D,     // kVK_RightOption
        Key.LWin => 0x37,         // kVK_Command
        Key.RWin => 0x36,         // kVK_RightCommand

        // CapsLock
        Key.CapsLock => 0x39,     // kVK_CapsLock

        _ => Unmapped,
    };

    /// <summary>
    /// Converts Avalonia key modifiers to Ghostty modifier flags.
    /// </summary>
    public static GhosttyMods ConvertModifiers(KeyModifiers keyModifiers)
    {
        var mods = GhosttyMods.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= GhosttyMods.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= GhosttyMods.Ctrl;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= GhosttyMods.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= GhosttyMods.Super;
        return mods;
    }
}
