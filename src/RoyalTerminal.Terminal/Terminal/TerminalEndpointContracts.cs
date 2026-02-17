// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Backend-neutral endpoint and input contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Backend-neutral terminal endpoint for basic session plumbing.
/// </summary>
public interface ITerminalEndpoint
{
    /// <summary>
    /// Sends UTF-8 text bytes to the endpoint.
    /// </summary>
    void SendText(ReadOnlySpan<byte> utf8);

    /// <summary>
    /// Updates endpoint focus state.
    /// </summary>
    void SetFocus(bool focused);

    /// <summary>
    /// Updates endpoint pixel size.
    /// </summary>
    void SetSize(int widthPx, int heightPx);
}

/// <summary>
/// Backend-neutral sink for input events.
/// </summary>
public interface ITerminalInputSink
{
    /// <summary>
    /// Sends a key input event.
    /// </summary>
    bool SendKey(TerminalKeyEvent keyEvent);

    /// <summary>
    /// Sends composed text input.
    /// </summary>
    bool SendText(string text);

    /// <summary>
    /// Sends pointer input.
    /// </summary>
    bool SendPointer(TerminalPointerEvent pointerEvent);
}

/// <summary>
/// Backend-neutral selection provider.
/// </summary>
public interface ITerminalSelectionSource
{
    /// <summary>
    /// Gets whether a selection currently exists.
    /// </summary>
    bool HasSelection { get; }

    /// <summary>
    /// Reads the current selection text.
    /// </summary>
    string? ReadSelection();
}

/// <summary>
/// Backend-neutral mode provider.
/// </summary>
public interface ITerminalModeSource
{
    /// <summary>
    /// Gets the latest mode-state snapshot.
    /// </summary>
    TerminalModeState ModeState { get; }

    /// <summary>
    /// Raised when mode state changes.
    /// </summary>
    event EventHandler<TerminalModeState>? ModeChanged;
}

/// <summary>
/// Optional endpoint capability for content scale updates.
/// </summary>
public interface ITerminalScaleSink
{
    /// <summary>
    /// Updates endpoint content scale.
    /// </summary>
    void SetContentScale(double scaleX, double scaleY);
}

/// <summary>
/// Normalized key-event action.
/// </summary>
public enum TerminalInputAction
{
    /// <summary>Key/button pressed.</summary>
    Press = 0,

    /// <summary>Key/button released.</summary>
    Release = 1,
}

/// <summary>
/// Normalized pointer event kind.
/// </summary>
public enum TerminalPointerEventKind
{
    /// <summary>Pointer move event.</summary>
    Move = 0,

    /// <summary>Pointer button event.</summary>
    Button = 1,

    /// <summary>Pointer wheel/scroll event.</summary>
    Scroll = 2,
}

/// <summary>
/// Normalized mouse/pointer button values.
/// </summary>
public enum TerminalMouseButton
{
    /// <summary>No button.</summary>
    None = 0,

    /// <summary>Left button.</summary>
    Left = 1,

    /// <summary>Middle button.</summary>
    Middle = 2,

    /// <summary>Right button.</summary>
    Right = 3,
}

/// <summary>
/// Normalized modifier flags.
/// </summary>
[Flags]
public enum TerminalModifiers
{
    /// <summary>No modifiers.</summary>
    None = 0,

    /// <summary>Shift key modifier.</summary>
    Shift = 1 << 0,

    /// <summary>Control key modifier.</summary>
    Control = 1 << 1,

    /// <summary>Alt/Option key modifier.</summary>
    Alt = 1 << 2,

    /// <summary>Meta/Super key modifier.</summary>
    Meta = 1 << 3,
}

/// <summary>
/// Backend-neutral key input payload.
/// </summary>
/// <param name="Action">Press/release action.</param>
/// <param name="KeyCode">Backend-defined key code identity.</param>
/// <param name="Text">Optional text payload for composed/printable input.</param>
/// <param name="Modifiers">Normalized key modifiers.</param>
/// <param name="IsComposing">Whether this event represents active IME composition.</param>
public readonly record struct TerminalKeyEvent(
    TerminalInputAction Action,
    uint KeyCode,
    string? Text,
    TerminalModifiers Modifiers,
    bool IsComposing = false);

/// <summary>
/// Backend-neutral pointer input payload.
/// </summary>
/// <param name="Kind">Pointer event kind.</param>
/// <param name="X">Pointer X coordinate in control space.</param>
/// <param name="Y">Pointer Y coordinate in control space.</param>
/// <param name="Button">Pointer button value for button events.</param>
/// <param name="Action">Pointer button press/release action for button events.</param>
/// <param name="Modifiers">Normalized key modifiers.</param>
/// <param name="DeltaX">Horizontal wheel delta for scroll events.</param>
/// <param name="DeltaY">Vertical wheel delta for scroll events.</param>
public readonly record struct TerminalPointerEvent(
    TerminalPointerEventKind Kind,
    double X,
    double Y,
    TerminalMouseButton Button,
    TerminalInputAction Action,
    TerminalModifiers Modifiers,
    double DeltaX = 0,
    double DeltaY = 0);
