// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Default platform keyboard input normalization.

using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

internal class DefaultTerminalKeyboardInputNormalizer : ITerminalKeyboardInputNormalizer
{
    public virtual TerminalKeyboardInputAction HandleKeyDown(KeyEventArgs e, in TerminalModeState modeState)
    {
        _ = e;
        _ = modeState;
        return TerminalKeyboardInputAction.Forward;
    }

    public virtual TerminalKeyboardInputAction HandleKeyUp(KeyEventArgs e, in TerminalModeState modeState)
    {
        _ = e;
        _ = modeState;
        return TerminalKeyboardInputAction.Forward;
    }
}
