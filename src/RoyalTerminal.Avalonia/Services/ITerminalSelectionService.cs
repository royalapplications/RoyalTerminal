// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Terminal selection service abstraction.

using Avalonia.Controls;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Handles selection operations such as copy, paste, and clearing selection state.
/// </summary>
public interface ITerminalSelectionService
{
    /// <summary>
    /// Copies selected text to the clipboard.
    /// </summary>
    Task CopySelectionAsync(
        Control owner,
        ITerminalSessionService sessionService,
        TerminalScreen? screen,
        SkiaTerminalRenderer? renderer);

    /// <summary>
    /// Pastes clipboard text using the provided send-input callback.
    /// </summary>
    Task PasteAsync(Control owner, Action<string> sendInput);

    /// <summary>
    /// Clears current selection and invalidates rendering.
    /// </summary>
    void ClearSelection(
        TerminalScreen? screen,
        SkiaTerminalRenderer? renderer,
        TerminalPresenter? presenter);
}
