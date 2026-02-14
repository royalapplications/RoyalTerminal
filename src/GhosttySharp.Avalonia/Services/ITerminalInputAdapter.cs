// Licensed under the MIT License.
// GhosttySharp.Avalonia — Terminal input adapter abstraction.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using GhosttySharp.Avalonia.Terminal;
using GhosttySharp.Native;

namespace GhosttySharp.Avalonia.Services;

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

    /// <summary>
    /// Converts Avalonia key modifiers into Ghostty modifier flags.
    /// </summary>
    GhosttyMods ConvertModifiers(KeyModifiers keyModifiers);

    /// <summary>
    /// Converts an Avalonia mouse button to Ghostty mouse button.
    /// </summary>
    GhosttyMouseButton ConvertMouseButton(MouseButton button);

    /// <summary>
    /// Resolves the pressed pointer button from Avalonia pointer properties.
    /// </summary>
    GhosttyMouseButton ConvertPressedMouseButton(PointerPointProperties properties);
}
