// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>Supplies layout metadata unavailable on Avalonia key events. Called on the input thread.</summary>
public interface ITerminalKeyboardLayout
{
    /// <summary>Reads the current layout without advancing the platform's dead-key/composition state.</summary>
    TerminalKeyboardLayoutInfo GetInfo(KeyEventArgs key);
}

/// <summary>Layout-derived encoding metadata; zero/None mean unavailable, never US-layout guesses.</summary>
/// <param name="UnshiftedCodepoint">Unicode scalar produced without modifiers.</param>
/// <param name="ConsumedModifiers">Modifiers used to produce the event's text, not shortcut modifiers.</param>
/// <param name="IsDeadKey">The key starts platform composition and must remain unhandled for the IME.</param>
public readonly record struct TerminalKeyboardLayoutInfo(uint UnshiftedCodepoint, TerminalModifiers ConsumedModifiers, bool IsDeadKey = false);
