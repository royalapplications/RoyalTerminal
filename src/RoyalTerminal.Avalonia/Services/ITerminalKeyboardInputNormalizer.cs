// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Platform keyboard input normalization.

using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

internal interface ITerminalKeyboardInputNormalizer
{
    TerminalKeyboardInputAction HandleKeyDown(KeyEventArgs e, in TerminalModeState modeState);

    TerminalKeyboardInputAction HandleKeyUp(KeyEventArgs e, in TerminalModeState modeState);
}

internal enum TerminalKeyboardInputAction
{
    Forward,
    SuppressForTextInput,
}
