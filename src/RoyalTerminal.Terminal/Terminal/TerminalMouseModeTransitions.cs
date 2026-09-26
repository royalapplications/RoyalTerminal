// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Ghostty's last-command mouse behavior, independent of stored DEC mode bits.</summary>
internal static class TerminalMouseModeTransitions
{
    internal static ReadOnlySpan<int> Modes => [9, 1000, 1002, 1003, 1005, 1006, 1015, 1016];

    internal static TerminalMouseModeState Apply(TerminalMouseModeState state, int mode, bool enabled) => mode switch
    {
        9 => state with { TrackingMode = enabled ? TerminalMouseTrackingMode.X10Press : TerminalMouseTrackingMode.None },
        1000 => state with { TrackingMode = enabled ? TerminalMouseTrackingMode.PressRelease : TerminalMouseTrackingMode.None },
        1002 => state with { TrackingMode = enabled ? TerminalMouseTrackingMode.ButtonMotion : TerminalMouseTrackingMode.None },
        1003 => state with { TrackingMode = enabled ? TerminalMouseTrackingMode.AnyMotion : TerminalMouseTrackingMode.None },
        1005 => state with { Encoding = enabled ? TerminalMouseEncoding.Utf8 : TerminalMouseEncoding.Default },
        1006 => state with { Encoding = enabled ? TerminalMouseEncoding.Sgr : TerminalMouseEncoding.Default },
        1015 => state with { Encoding = enabled ? TerminalMouseEncoding.Urxvt : TerminalMouseEncoding.Default },
        1016 => state with { Encoding = enabled ? TerminalMouseEncoding.SgrPixels : TerminalMouseEncoding.Default },
        _ => state,
    };
}
