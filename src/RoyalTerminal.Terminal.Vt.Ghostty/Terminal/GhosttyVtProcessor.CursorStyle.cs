// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private TerminalCursorStyle _defaultCursorStyle;
    private bool _defaultCursorBlink;

    /// <inheritdoc />
    public void SetDefaultCursorStyle(TerminalCursorStyle style)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyCursorStyle native = style switch
        {
            TerminalCursorStyle.Block => GhosttyVtNative.GhosttyCursorStyle.Block,
            TerminalCursorStyle.Underline => GhosttyVtNative.GhosttyCursorStyle.Underline,
            TerminalCursorStyle.Bar => GhosttyVtNative.GhosttyCursorStyle.Bar,
            TerminalCursorStyle.BlockHollow => GhosttyVtNative.GhosttyCursorStyle.BlockHollow,
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
        _terminal.SetDefaultCursorStyle(native);
        _defaultCursorStyle = style;
        _sixelOverlayProcessor?.SetDefaultCursorStyle(style);
        RefreshStateAndScreenFromNative();
    }

    /// <inheritdoc />
    public void SetDefaultCursorBlink(bool blink)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _terminal.SetDefaultCursorBlink(blink);
        _defaultCursorBlink = blink;
        _sixelOverlayProcessor?.SetDefaultCursorBlink(blink);
        RefreshStateAndScreenFromNative();
    }
}
