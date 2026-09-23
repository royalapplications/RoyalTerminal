// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>Copies effective mouse encoder state, independent of DEC mode bits. Serialize with terminal mutation.</summary>
    public TerminalMouseModeState GetMouseModeState() => GetMouseInputState().Modes;

    /// <summary>Copies effective mouse modes and application capture policy. Serialize with terminal mutation.</summary>
    public unsafe TerminalMouseInputState GetMouseInputState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.RoyalMouseState state = GhosttyVtNative.RoyalMouseState.CreateSized();
        ThrowIfFailed(GhosttyVtNative.MouseState(_handle, &state), "ghostty_royal_mouse_state");
        return new(new((TerminalMouseTrackingMode)state.Tracking, (TerminalMouseEncoding)state.Format),
            state.ShiftCapture == 0 ? null : state.ShiftCapture == 2);
    }

    /// <summary>Sets the application Shift capture override without replaying VT; null restores host policy.</summary>
    public void SetMouseShiftCapture(bool? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.MouseShiftCaptureSet(_handle, value is null ? 0U : value.Value ? 2U : 1U),
            "ghostty_royal_mouse_shift_capture_set");
    }
}
