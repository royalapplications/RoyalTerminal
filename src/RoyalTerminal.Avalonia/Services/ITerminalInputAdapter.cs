// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Terminal input adapter abstraction.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Maps Avalonia input events to terminal input protocol messages.
/// </summary>
public interface ITerminalInputAdapter
{
    /// <summary>
    /// Handles key press input for the current terminal session.
    /// </summary>
    bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor);

    /// <summary>
    /// Handles key release input for the current terminal session.
    /// </summary>
    bool HandleKeyUp(KeyEventArgs e, ITerminalSessionService sessionService);

    /// <summary>
    /// Handles text input for the current terminal session.
    /// </summary>
    bool HandleTextInput(TextInputEventArgs e, ITerminalSessionService sessionService);
}
