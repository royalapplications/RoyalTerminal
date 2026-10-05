// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

// Preserve keypad identity without guessing layout characters from US key positions.
// Avalonia's logical Enter/navigation keys otherwise erase Ghostty's Kitty distinction.
internal static class TerminalKeyEncodingIdentity
{
    internal static string Get(KeyEventArgs e, bool hasLayoutCodepoint = false) => (e.PhysicalKey, e.Key) switch
    {
        (PhysicalKey.NumPadEnter, _) => "NumPadEnter",
        (PhysicalKey.NumPadEqual, _) => "NumPadEqual",
        (PhysicalKey.NumPad0, Key.Insert) => "NumPadInsert",
        (PhysicalKey.NumPad1, Key.End) => "NumPadEnd",
        (PhysicalKey.NumPad2, Key.Down) => "NumPadDown",
        (PhysicalKey.NumPad3, Key.PageDown) => "NumPadPageDown",
        (PhysicalKey.NumPad4, Key.Left) => "NumPadLeft",
        (PhysicalKey.NumPad5, Key.Clear) => "NumPadBegin",
        (PhysicalKey.NumPad6, Key.Right) => "NumPadRight",
        (PhysicalKey.NumPad7, Key.Home) => "NumPadHome",
        (PhysicalKey.NumPad8, Key.Up) => "NumPadUp",
        (PhysicalKey.NumPad9, Key.PageUp) => "NumPadPageUp",
        (PhysicalKey.NumPadDecimal, Key.Delete) => "NumPadDelete",
        // With an authoritative layout scalar, retain the physical identity
        // separately for Kitty's base-layout alternate. Without one preserve
        // the logical fallback; a US position is not a substitute for layout.
        _ => hasLayoutCodepoint && e.PhysicalKey.ToQwertyKey() is { } physical && physical != Key.None
            ? physical.ToString() : e.Key.ToString(),
    };
}
