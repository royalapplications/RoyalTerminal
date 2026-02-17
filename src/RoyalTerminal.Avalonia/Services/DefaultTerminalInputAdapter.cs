// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default terminal input adapter.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Default implementation for terminal keyboard and mouse input mapping.
/// </summary>
public sealed class DefaultTerminalInputAdapter : ITerminalInputAdapter
{
    /// <inheritdoc />
    public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is not null)
        {
            TerminalKeyEvent keyEvent = new(
                TerminalInputAction.Press,
                KeyCode: (uint)e.Key,
                Text: null,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
                IsComposing: false);
            return inputSink.SendKey(keyEvent);
        }

        if (sessionService.HasActiveTransport || sessionService.HasPty)
        {
            string? sequence = KeyToAnsiSequence(
                e.Key,
                e.KeyModifiers,
                vtProcessor?.ApplicationCursorKeys ?? false);
            if (sequence is not null)
            {
                sessionService.SendInput(sequence);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool HandleKeyUp(KeyEventArgs e, ITerminalSessionService sessionService)
    {
        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is null)
        {
            return false;
        }

        TerminalKeyEvent keyEvent = new(
            TerminalInputAction.Release,
            KeyCode: (uint)e.Key,
            Text: null,
            Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
            IsComposing: false);

        return inputSink.SendKey(keyEvent);
    }

    /// <inheritdoc />
    public bool HandleTextInput(TextInputEventArgs e, ITerminalSessionService sessionService)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return false;
        }

        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is not null)
        {
            return inputSink.SendText(e.Text);
        }

        sessionService.SendInput(e.Text);
        return sessionService.HasActiveTransport || sessionService.HasPty;
    }

    private static TerminalModifiers ConvertTerminalModifiers(KeyModifiers keyModifiers)
    {
        TerminalModifiers mods = TerminalModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= TerminalModifiers.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= TerminalModifiers.Control;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= TerminalModifiers.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= TerminalModifiers.Meta;
        return mods;
    }

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
