// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private TerminalCursorStyle _primaryCursorStyle;
    private TerminalCursorStyle _alternateCursorStyle;
    private TerminalCursorStyle _defaultCursorStyle;
    private bool? _defaultCursorBlink = false;
    private bool _cursorIsDefault = true;
    private ref TerminalCursorStyle ActiveCursorStyle => ref (_inAltScreen ? ref _alternateCursorStyle : ref _primaryCursorStyle);

    /// <inheritdoc />
    public void SetDefaultCursorStyle(TerminalCursorStyle style)
    {
        if ((byte)style > (byte)TerminalCursorStyle.BlockHollow) throw new ArgumentOutOfRangeException(nameof(style));
        _defaultCursorStyle = style;
        if (_cursorIsDefault) SetCursorStyle(0);
    }

    /// <inheritdoc />
    public void SetDefaultCursorBlink(bool blink)
    {
        _defaultCursorBlink = blink;
        if (_cursorIsDefault) SetCursorStyle(0);
    }

    private void SetCursorStyle(int parameter)
    {
        if ((uint)parameter > 6) return;
        _cursorIsDefault = parameter == 0;
        ActiveCursorStyle = parameter switch
        {
            0 => _defaultCursorStyle,
            3 or 4 => TerminalCursorStyle.Underline,
            5 or 6 => TerminalCursorStyle.Bar,
            _ => TerminalCursorStyle.Block,
        };
        SetExtendedDecMode(12, parameter == 0 ? _defaultCursorBlink ?? true : (parameter & 1) != 0);
    }

    private int CursorStyleReport => (ActiveCursorStyle switch
    {
        TerminalCursorStyle.Underline => 4,
        TerminalCursorStyle.Bar => 6,
        _ => 2, // Ghostty's hollow block is reported as ordinary block.
    }) - (_extendedDecModesEnabled.Contains(12) ? 1 : 0);

    internal (TerminalCursorStyle Style, bool? Blink, bool IsDefault) SnapshotCursorPolicy
        => (_defaultCursorStyle, _defaultCursorBlink, _cursorIsDefault);

    internal TerminalCursorStyle GetSnapshotCursorStyle(int key) => key switch
    {
        0 => _primaryCursorStyle,
        1 => _alternateCursorStyle,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    // Raw unpublished installation: do not select defaults or overwrite mode 12.
    internal void InstallSnapshotCursorPolicy(GhosttySnapshotTerminalHeader header)
    {
        _defaultCursorStyle = FromSnapshotCursorStyle(header.CursorDefaultStyle);
        _defaultCursorBlink = header.CursorDefaultBlink;
        _cursorIsDefault = header.CursorIsDefault;
    }

    internal void InstallSnapshotCursorStyle(GhosttySnapshotScreenState screen)
    {
        TerminalCursorStyle style = FromSnapshotCursorStyle(screen.CursorStyle);
        if (screen.Key == 0) _primaryCursorStyle = style;
        else _alternateCursorStyle = style;
    }

    private static TerminalCursorStyle FromSnapshotCursorStyle(byte style) => style switch
    {
        0 => TerminalCursorStyle.Bar,
        2 => TerminalCursorStyle.Underline,
        3 => TerminalCursorStyle.BlockHollow,
        _ => TerminalCursorStyle.Block,
    };
}
