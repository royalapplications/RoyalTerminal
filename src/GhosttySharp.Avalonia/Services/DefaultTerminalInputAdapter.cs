// Licensed under the MIT License.
// GhosttySharp.Avalonia — Default terminal input adapter.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using GhosttySharp.Avalonia.Terminal;
using GhosttySharp.Terminal.Services;
using GhosttySharp.Native;

namespace GhosttySharp.Avalonia.Services;

/// <summary>
/// Default implementation for terminal keyboard and mouse input mapping.
/// </summary>
public sealed class DefaultTerminalInputAdapter : ITerminalInputAdapter
{
    /// <inheritdoc />
    public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        if (sessionService.Surface is not null)
        {
            GhosttyMods mods = ConvertModifiers(e.KeyModifiers);
            GhosttyKey key = ConvertKey(e.Key);
            GhosttyInputKey inputKey = new()
            {
                Action = GhosttyInputAction.Press,
                Keycode = (uint)key,
                Mods = mods,
                Composing = false,
            };

            sessionService.Surface.SendKey(inputKey);
            return true;
        }

        if (sessionService.Pty is not null)
        {
            string? sequence = KeyToAnsiSequence(
                e.Key,
                e.KeyModifiers,
                vtProcessor?.ApplicationCursorKeys ?? false);
            if (sequence is not null)
            {
                sessionService.Pty.Write(sequence);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool HandleKeyUp(KeyEventArgs e, ITerminalSessionService sessionService)
    {
        if (sessionService.Surface is null)
        {
            return false;
        }

        GhosttyMods mods = ConvertModifiers(e.KeyModifiers);
        GhosttyKey key = ConvertKey(e.Key);
        GhosttyInputKey inputKey = new()
        {
            Action = GhosttyInputAction.Release,
            Keycode = (uint)key,
            Mods = mods,
            Composing = false,
        };

        sessionService.Surface.SendKey(inputKey);
        return true;
    }

    /// <inheritdoc />
    public bool HandleTextInput(TextInputEventArgs e, ITerminalSessionService sessionService)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return false;
        }

        if (sessionService.Surface is not null)
        {
            sessionService.Surface.SendText(e.Text);
            return true;
        }

        if (sessionService.Pty is not null)
        {
            sessionService.Pty.Write(e.Text);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public GhosttyMods ConvertModifiers(KeyModifiers keyModifiers)
    {
        GhosttyMods mods = GhosttyMods.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= GhosttyMods.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= GhosttyMods.Ctrl;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= GhosttyMods.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= GhosttyMods.Super;
        return mods;
    }

    /// <inheritdoc />
    public GhosttyMouseButton ConvertMouseButton(MouseButton button)
        => button switch
        {
            MouseButton.Left => GhosttyMouseButton.Left,
            MouseButton.Right => GhosttyMouseButton.Right,
            MouseButton.Middle => GhosttyMouseButton.Middle,
            _ => GhosttyMouseButton.Left,
        };

    /// <inheritdoc />
    public GhosttyMouseButton ConvertPressedMouseButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed) return GhosttyMouseButton.Left;
        if (properties.IsRightButtonPressed) return GhosttyMouseButton.Right;
        if (properties.IsMiddleButtonPressed) return GhosttyMouseButton.Middle;
        return GhosttyMouseButton.Left;
    }

    private static GhosttyKey ConvertKey(Key key)
        => key switch
        {
            Key.A => GhosttyKey.A,
            Key.B => GhosttyKey.B,
            Key.C => GhosttyKey.C,
            Key.D => GhosttyKey.D,
            Key.E => GhosttyKey.E,
            Key.F => GhosttyKey.F,
            Key.G => GhosttyKey.G,
            Key.H => GhosttyKey.H,
            Key.I => GhosttyKey.I,
            Key.J => GhosttyKey.J,
            Key.K => GhosttyKey.K,
            Key.L => GhosttyKey.L,
            Key.M => GhosttyKey.M,
            Key.N => GhosttyKey.N,
            Key.O => GhosttyKey.O,
            Key.P => GhosttyKey.P,
            Key.Q => GhosttyKey.Q,
            Key.R => GhosttyKey.R,
            Key.S => GhosttyKey.S,
            Key.T => GhosttyKey.T,
            Key.U => GhosttyKey.U,
            Key.V => GhosttyKey.V,
            Key.W => GhosttyKey.W,
            Key.X => GhosttyKey.X,
            Key.Y => GhosttyKey.Y,
            Key.Z => GhosttyKey.Z,
            Key.D0 => GhosttyKey.Digit0,
            Key.D1 => GhosttyKey.Digit1,
            Key.D2 => GhosttyKey.Digit2,
            Key.D3 => GhosttyKey.Digit3,
            Key.D4 => GhosttyKey.Digit4,
            Key.D5 => GhosttyKey.Digit5,
            Key.D6 => GhosttyKey.Digit6,
            Key.D7 => GhosttyKey.Digit7,
            Key.D8 => GhosttyKey.Digit8,
            Key.D9 => GhosttyKey.Digit9,
            Key.Return => GhosttyKey.Enter,
            Key.Escape => GhosttyKey.Escape,
            Key.Back => GhosttyKey.Backspace,
            Key.Tab => GhosttyKey.Tab,
            Key.Space => GhosttyKey.Space,
            Key.OemMinus => GhosttyKey.Minus,
            Key.OemPlus => GhosttyKey.Equal,
            Key.OemOpenBrackets => GhosttyKey.BracketLeft,
            Key.OemCloseBrackets => GhosttyKey.BracketRight,
            Key.OemBackslash => GhosttyKey.Backslash,
            Key.OemSemicolon => GhosttyKey.Semicolon,
            Key.OemQuotes => GhosttyKey.Quote,
            Key.OemTilde => GhosttyKey.Backquote,
            Key.OemComma => GhosttyKey.Comma,
            Key.OemPeriod => GhosttyKey.Period,
            Key.Oem2 => GhosttyKey.Slash,
            Key.F1 => GhosttyKey.F1,
            Key.F2 => GhosttyKey.F2,
            Key.F3 => GhosttyKey.F3,
            Key.F4 => GhosttyKey.F4,
            Key.F5 => GhosttyKey.F5,
            Key.F6 => GhosttyKey.F6,
            Key.F7 => GhosttyKey.F7,
            Key.F8 => GhosttyKey.F8,
            Key.F9 => GhosttyKey.F9,
            Key.F10 => GhosttyKey.F10,
            Key.F11 => GhosttyKey.F11,
            Key.F12 => GhosttyKey.F12,
            Key.Insert => GhosttyKey.Insert,
            Key.Home => GhosttyKey.Home,
            Key.PageUp => GhosttyKey.PageUp,
            Key.Delete => GhosttyKey.Delete,
            Key.End => GhosttyKey.End,
            Key.PageDown => GhosttyKey.PageDown,
            Key.Right => GhosttyKey.ArrowRight,
            Key.Left => GhosttyKey.ArrowLeft,
            Key.Down => GhosttyKey.ArrowDown,
            Key.Up => GhosttyKey.ArrowUp,
            Key.NumPad0 => GhosttyKey.Numpad0,
            Key.NumPad1 => GhosttyKey.Numpad1,
            Key.NumPad2 => GhosttyKey.Numpad2,
            Key.NumPad3 => GhosttyKey.Numpad3,
            Key.NumPad4 => GhosttyKey.Numpad4,
            Key.NumPad5 => GhosttyKey.Numpad5,
            Key.NumPad6 => GhosttyKey.Numpad6,
            Key.NumPad7 => GhosttyKey.Numpad7,
            Key.NumPad8 => GhosttyKey.Numpad8,
            Key.NumPad9 => GhosttyKey.Numpad9,
            Key.Decimal => GhosttyKey.NumpadDecimal,
            Key.Divide => GhosttyKey.NumpadDivide,
            Key.Multiply => GhosttyKey.NumpadMultiply,
            Key.Subtract => GhosttyKey.NumpadSubtract,
            Key.Add => GhosttyKey.NumpadAdd,
            Key.LeftShift => GhosttyKey.ShiftLeft,
            Key.LeftCtrl => GhosttyKey.ControlLeft,
            Key.LeftAlt => GhosttyKey.AltLeft,
            Key.LWin => GhosttyKey.MetaLeft,
            Key.RightShift => GhosttyKey.ShiftRight,
            Key.RightCtrl => GhosttyKey.ControlRight,
            Key.RightAlt => GhosttyKey.AltRight,
            Key.RWin => GhosttyKey.MetaRight,
            _ => GhosttyKey.Unidentified,
        };

    private static string? KeyToAnsiSequence(Key key, KeyModifiers modifiers, bool applicationCursorKeys)
    {
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);

        if (ctrl)
        {
            return key switch
            {
                Key.C => "\x03",
                Key.D => "\x04",
                Key.Z => "\x1A",
                Key.L => "\x0C",
                Key.A => "\x01",
                Key.E => "\x05",
                Key.K => "\x0B",
                Key.U => "\x15",
                Key.W => "\x17",
                Key.R => "\x12",
                _ => null,
            };
        }

        string csi = applicationCursorKeys ? "\x1BO" : "\x1B[";
        return key switch
        {
            Key.Return => "\r",
            Key.Back => "\x7F",
            Key.Escape => "\x1B",
            Key.Tab => "\t",
            Key.Up => csi + "A",
            Key.Down => csi + "B",
            Key.Right => csi + "C",
            Key.Left => csi + "D",
            Key.Home => applicationCursorKeys ? "\x1BOH" : "\x1B[H",
            Key.End => applicationCursorKeys ? "\x1BOF" : "\x1B[F",
            Key.Insert => "\x1B[2~",
            Key.Delete => "\x1B[3~",
            Key.PageUp => "\x1B[5~",
            Key.PageDown => "\x1B[6~",
            Key.F1 => "\x1BOP",
            Key.F2 => "\x1BOQ",
            Key.F3 => "\x1BOR",
            Key.F4 => "\x1BOS",
            Key.F5 => "\x1B[15~",
            Key.F6 => "\x1B[17~",
            Key.F7 => "\x1B[18~",
            Key.F8 => "\x1B[19~",
            Key.F9 => "\x1B[20~",
            Key.F10 => "\x1B[21~",
            Key.F11 => "\x1B[23~",
            Key.F12 => "\x1B[24~",
            _ => null,
        };
    }
}
