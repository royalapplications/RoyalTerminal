// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>Copies effective mouse encoder state, independent of DEC mode bits. Serialize with terminal mutation.</summary>
    public unsafe TerminalMouseModeState GetMouseModeState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.RoyalMouseState state = GhosttyVtNative.RoyalMouseState.CreateSized();
        ThrowIfFailed(GhosttyVtNative.MouseState(_handle, &state), "ghostty_royal_mouse_state");
        return new((TerminalMouseTrackingMode)state.Tracking, (TerminalMouseEncoding)state.Format);
    }
}
