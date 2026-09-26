// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

internal sealed class TerminalKeyboardLayout : ITerminalKeyboardLayout
{
    public TerminalKeyboardLayoutInfo GetInfo(KeyEventArgs key)
    {
        if (OperatingSystem.IsWindows())
            return Resolve(key, WindowsKeyboardLayoutTextInputProbe.Translate) with { IsDeadKey = WindowsKeyboardLayoutTextInputProbe.IsDeadKey(key) };
        if (OperatingSystem.IsMacOS() && global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return Resolve(key, MacOsKeyboardLayout.Translate) with { IsDeadKey = MacOsKeyboardLayout.IsDeadKey(key) };
        // Avalonia's Wayland/X11 event has no native keymap or consumed mask.
        // Hosts with that metadata can inject ITerminalKeyboardLayout. Do not
        // manufacture an unshifted symbol by applying US physical-key rules.
        return new(key.KeyModifiers == KeyModifiers.None ? Scalar(key.KeySymbol) : 0, TerminalModifiers.None);
    }

    internal static TerminalKeyboardLayoutInfo Resolve(KeyEventArgs key, Func<KeyEventArgs, KeyModifiers, string?> translate)
    {
        uint unshifted = Scalar(translate(key, KeyModifiers.None));
        TerminalModifiers consumed = TerminalModifiers.None;
        string? text = key.KeySymbol;
        if (Scalar(text) == 0 || translate(key, key.KeyModifiers) != text) return new(unshifted, consumed);
        // Modifiers are consumed only when removing them changes the produced
        // character. Ctrl/Alt shortcuts whose text is unchanged remain intact.
        if ((key.KeyModifiers & KeyModifiers.Shift) != 0 && translate(key, key.KeyModifiers & ~KeyModifiers.Shift) != text)
            consumed |= TerminalModifiers.Shift;
        if ((key.KeyModifiers & KeyModifiers.Alt) != 0 && translate(key, key.KeyModifiers & ~KeyModifiers.Alt) != text)
            consumed |= TerminalModifiers.Alt;
        if ((key.KeyModifiers & KeyModifiers.Control) != 0 && translate(key, key.KeyModifiers & ~KeyModifiers.Control) != text)
            consumed |= TerminalModifiers.Control;
        return new(unshifted, consumed);
    }

    internal static uint Scalar(string? text) => text is not null && Rune.TryGetRuneAt(text, 0, out Rune rune) &&
        rune.Utf16SequenceLength == text.Length && !Rune.IsControl(rune) ? (uint)rune.Value : 0;
}
