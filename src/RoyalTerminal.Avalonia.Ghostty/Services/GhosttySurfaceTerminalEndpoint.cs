// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Ghostty surface adapter for terminal session services.

using RoyalTerminal.Terminal;
using Avalonia.Input;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Adapts <see cref="GhosttySurface"/> to backend-neutral terminal endpoint capabilities.
/// </summary>
public sealed class GhosttySurfaceTerminalEndpoint :
    ITerminalEndpoint,
    ITerminalInputSink,
    ITerminalSelectionSource,
    ITerminalScaleSink
{
    /// <summary>
    /// Creates a new endpoint wrapper for a Ghostty surface.
    /// </summary>
    public GhosttySurfaceTerminalEndpoint(GhosttySurface surface)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    /// <summary>
    /// Gets the wrapped Ghostty surface.
    /// </summary>
    public GhosttySurface Surface { get; }

    /// <inheritdoc />
    public bool HasSelection => !string.IsNullOrEmpty(Surface.ReadSelection());

    /// <inheritdoc />
    public void SendText(ReadOnlySpan<byte> utf8)
    {
        Surface.SendText(utf8);
    }

    /// <inheritdoc />
    public void SetFocus(bool focused)
    {
        Surface.SetFocus(focused);
    }

    /// <inheritdoc />
    public void SetSize(int widthPx, int heightPx)
    {
        uint safeWidth = widthPx > 0 ? (uint)widthPx : 1u;
        uint safeHeight = heightPx > 0 ? (uint)heightPx : 1u;
        Surface.SetSize(safeWidth, safeHeight);
    }

    /// <inheritdoc />
    public bool SendKey(TerminalKeyEvent keyEvent)
    {
        GhosttyInputKey inputKey = new()
        {
            Action = keyEvent.Action == TerminalInputAction.Release
                ? GhosttyInputAction.Release
                : GhosttyInputAction.Press,
            Keycode = (uint)ToGhosttyKey(keyEvent.KeyCode),
            Mods = ToGhosttyMods(keyEvent.Modifiers),
            Composing = keyEvent.IsComposing,
        };

        return Surface.SendKey(inputKey);
    }

    /// <inheritdoc />
    public bool SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        Surface.SendText(text);
        return true;
    }

    /// <inheritdoc />
    public bool SendPointer(TerminalPointerEvent pointerEvent)
    {
        GhosttyMods mods = ToGhosttyMods(pointerEvent.Modifiers);

        switch (pointerEvent.Kind)
        {
            case TerminalPointerEventKind.Move:
                Surface.SendMousePos(pointerEvent.X, pointerEvent.Y, mods);
                return true;

            case TerminalPointerEventKind.Button:
            {
                if (pointerEvent.Button == TerminalMouseButton.None)
                {
                    return false;
                }

                GhosttyMouseState state = pointerEvent.Action == TerminalInputAction.Release
                    ? GhosttyMouseState.Release
                    : GhosttyMouseState.Press;
                return Surface.SendMouseButton(state, ToGhosttyMouseButton(pointerEvent.Button), mods);
            }

            case TerminalPointerEventKind.Scroll:
                Surface.SendMouseScroll(pointerEvent.DeltaX, pointerEvent.DeltaY, (int)mods);
                return true;

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public string? ReadSelection()
    {
        return Surface.ReadSelection();
    }

    /// <inheritdoc />
    public void SetContentScale(double scaleX, double scaleY)
    {
        Surface.SetContentScale(scaleX, scaleY);
    }

    private static GhosttyMouseButton ToGhosttyMouseButton(TerminalMouseButton button) =>
        button switch
        {
            TerminalMouseButton.Left => GhosttyMouseButton.Left,
            TerminalMouseButton.Middle => GhosttyMouseButton.Middle,
            TerminalMouseButton.Right => GhosttyMouseButton.Right,
            _ => GhosttyMouseButton.Left,
        };

    private static GhosttyMods ToGhosttyMods(TerminalModifiers modifiers)
    {
        GhosttyMods result = GhosttyMods.None;
        if (modifiers.HasFlag(TerminalModifiers.Shift)) result |= GhosttyMods.Shift;
        if (modifiers.HasFlag(TerminalModifiers.Control)) result |= GhosttyMods.Ctrl;
        if (modifiers.HasFlag(TerminalModifiers.Alt)) result |= GhosttyMods.Alt;
        if (modifiers.HasFlag(TerminalModifiers.Meta)) result |= GhosttyMods.Super;
        return result;
    }

    private static GhosttyKey ToGhosttyKey(uint keyCode)
    {
        Key key = (Key)keyCode;
        return key switch
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
    }
}
